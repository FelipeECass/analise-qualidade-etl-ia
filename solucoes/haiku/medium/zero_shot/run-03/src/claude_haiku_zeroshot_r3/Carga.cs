using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ClaudeHaikuZeroshotR3;

/// <summary>
/// Lê os ZIPs de licenciamento, aplica limpeza, rejeição, deduplicação e grava
/// as tabelas estacao e emissao (seções 2, 3, 4, 5, 6 e 7 da especificação).
/// </summary>
internal sealed class Carga : IDisposable
{
    private const char Sep = '\u001f';

    private readonly SqliteConnection _conn;
    private readonly Insercao _insEstacao;
    private readonly Insercao _insEmissao;

    // Chaves de estação (texto) e de emissão (hash de 128 bits do texto da chave,
    // para não manter milhões de strings longas na memória).
    private readonly HashSet<string> _estacoes = new(StringComparer.Ordinal);
    private readonly HashSet<(ulong, ulong)> _emissoes = new();

    public long LinhasLidas { get; private set; }
    public long LinhasRejeitadas { get; private set; }
    public long EstacoesGravadas { get; private set; }
    public long EmissoesGravadas { get; private set; }
    public long EmissoesDuplicadas { get; private set; }

    public Carga(SqliteConnection conn)
    {
        _conn = conn;

        _insEstacao = new Insercao(conn, "estacao",
            "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco",
            "latitude", "longitude", "coordenada_valida", "entidade", "operadora");

        _insEmissao = new Insercao(conn, "emissao",
            "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao",
            "designacao_emissao", "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz",
            "duplex_mhz", "azimute", "potencia_w", "ganho_antena", "altura_antena_m",
            "data_licenciamento", "data_primeiro_licenciamento", "data_validade",
            "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
            "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db",
            "cod_tipo_antena", "cod_equipamento_antena", "cod_equipamento_transmissor",
            "potencia_dbm", "azimute_valido", "elevacao_valida");
    }

    public void CarregarZip(string caminhoZip)
    {
        using var zip = ZipFile.OpenRead(caminhoZip);
        var entrada = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Nenhum arquivo CSV em {Path.GetFileName(caminhoZip)}.");

        using var leitor = new StreamReader(entrada.Open(), new UTF8Encoding(false), true, 1 << 20);
        using var tx = _conn.BeginTransaction();
        _insEstacao.DefinirTransacao(tx);
        _insEmissao.DefinirTransacao(tx);

        Colunas? colunas = null;
        foreach (var registro in CsvReader.Read(leitor))
        {
            if (colunas is null)
            {
                colunas = Colunas.DoCabecalho(registro);
                continue;
            }
            ProcessarRegistro(registro, colunas);
        }

        tx.Commit();
    }

    private void ProcessarRegistro(string[] r, Colunas c)
    {
        LinhasLidas++;

        string? Campo(int i) => i < r.Length ? r[i] : null;

        // Seção 2: rejeição apenas por chave nula.
        var uf = Limpeza.Maiusculas(Campo(c.SiglaUf));
        var fistel = Limpeza.Texto(Campo(c.NumFistel));
        var numEstacao = Limpeza.Texto(Campo(c.NumEstacao));
        if (uf is null || fistel is null || numEstacao is null)
        {
            LinhasRejeitadas++;
            return;
        }

        // Estação: uma linha por (uf, fistel, num_estacao); atributos da primeira linha.
        var chaveEstacao = string.Join(Sep, uf, fistel, numEstacao);
        if (_estacoes.Add(chaveEstacao))
        {
            var entidade = Limpeza.NormalizaTexto(Campo(c.NomeEntidade));
            var latitude = Limpeza.Numero(Campo(c.Latitude));
            var longitude = Limpeza.Numero(Campo(c.Longitude));

            _insEstacao.Executar(
                fistel,
                numEstacao,
                uf,
                Limpeza.Texto(Campo(c.CodMunicipio)),
                Limpeza.Texto(Campo(c.NomeMunicipio)),
                Limpeza.NormalizaTexto(Campo(c.EnderecoEstacao)),
                Dbl(latitude),
                Dbl(longitude),
                Derivados.CoordenadaValida(latitude, longitude),
                entidade,
                Derivados.Operadora(entidade));

            EstacoesGravadas++;
        }

        // Emissão: deduplicação pelos valores já tratados.
        var numAto = Limpeza.Texto(Campo(c.NumAto));
        var tecnologia = Limpeza.Maiusculas(Campo(c.Tecnologia));
        var designacao = Limpeza.Maiusculas(Campo(c.DesignacaoEmissao));
        var freqTx = Limpeza.Numero(Campo(c.FreqTxMHz));
        var freqRx = Limpeza.Numero(Campo(c.FreqRxMHz));
        var azimute = Limpeza.Numero(Campo(c.Azimute));

        var chaveEmissao = string.Join(Sep,
            TextoChave(uf), TextoChave(fistel), TextoChave(numEstacao), TextoChave(numAto),
            NumeroChave(azimute), NumeroChave(freqTx), NumeroChave(freqRx),
            TextoChave(designacao), TextoChave(tecnologia));

        if (!_emissoes.Add(Digest(chaveEmissao)))
        {
            EmissoesDuplicadas++;
            return;
        }

        var geracao = Derivados.Geracao(tecnologia);
        var largura = Derivados.LarguraBandaKhz(designacao);
        var duplex = Derivados.DuplexMhz(freqTx, freqRx);
        var potenciaW = Limpeza.Numero(Campo(c.PotenciaTransmissorWatts));
        var potenciaDbm = Derivados.PotenciaDbm(potenciaW);

        var dataLicenciamento = Limpeza.Data(Campo(c.DataLicenciamento));
        var dataPrimeiro = Limpeza.Data(Campo(c.DataPrimeiroLicenciamento));
        var dataValidade = Limpeza.Data(Campo(c.DataValidade));
        var vencida = Derivados.Vencida(dataValidade);
        var anos = Derivados.AnosLicenciada(dataPrimeiro);

        var ganho = Limpeza.Numero(Campo(c.GanhoAntena));
        var altura = Limpeza.Numero(Campo(c.AlturaAntena));
        var anguloElevacao = Limpeza.Numero(Campo(c.AnguloElevacao));
        var anguloMeia = Limpeza.Numero(Campo(c.AnguloMeiaPotenciaAntena));
        var frenteCosta = Limpeza.Numero(Campo(c.FrenteCostaAntena));

        _insEmissao.Executar(
            uf,
            fistel,
            numEstacao,
            numAto,
            tecnologia,
            geracao,
            designacao,
            largura,
            Dbl(freqTx),
            Dbl(freqRx),
            duplex,
            Dbl(azimute),
            Dbl(potenciaW),
            Dbl(ganho),
            Dbl(altura),
            Iso(dataLicenciamento),
            Iso(dataPrimeiro),
            Iso(dataValidade),
            vencida,
            anos,
            Limpeza.Texto(Campo(c.Status)),
            Limpeza.Texto(Campo(c.ClasseEstacao)),
            Limpeza.Maiusculas(Campo(c.Polarizacao)),
            Dbl(anguloElevacao),
            Dbl(anguloMeia),
            Dbl(frenteCosta),
            Limpeza.Texto(Campo(c.CodTipoAntena)),
            Limpeza.Texto(Campo(c.CodEquipamentoAntena)),
            Limpeza.Texto(Campo(c.CodEquipamentoTransmissor)),
            potenciaDbm,
            Derivados.AzimuteValido(azimute),
            Derivados.ElevacaoValida(anguloElevacao));

        EmissoesGravadas++;
    }

    private static string TextoChave(string? valor) => valor is null ? "N" : "T" + valor;

    private static string NumeroChave(decimal? valor)
        => valor is null ? "N" : "V" + valor.Value.ToString("G29", CultureInfo.InvariantCulture);

    private static (ulong, ulong) Digest(string chave)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(chave));
        return (BitConverter.ToUInt64(hash, 0), BitConverter.ToUInt64(hash, 8));
    }

    private static double? Dbl(decimal? valor) => valor is null ? null : (double)valor.Value;

    private static string? Iso(DateOnly? data) => data?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _insEstacao.Dispose();
        _insEmissao.Dispose();
    }

    /// <summary>Posições das colunas de interesse, localizadas pelo nome do cabeçalho.</summary>
    private sealed class Colunas
    {
        public int NumFistel, NumEstacao, SiglaUf, NumAto, CodMunicipio, NomeMunicipio,
            NomeEntidade, EnderecoEstacao, Latitude, Longitude, Tecnologia, DesignacaoEmissao,
            FreqTxMHz, FreqRxMHz, Azimute, ClasseEstacao, PotenciaTransmissorWatts,
            GanhoAntena, AlturaAntena, AnguloElevacao, AnguloMeiaPotenciaAntena,
            FrenteCostaAntena, Polarizacao, CodTipoAntena, CodEquipamentoAntena,
            CodEquipamentoTransmissor, Status, DataLicenciamento,
            DataPrimeiroLicenciamento, DataValidade;

        public static Colunas DoCabecalho(string[] cabecalho)
        {
            var posicoes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < cabecalho.Length; i++)
            {
                var nome = cabecalho[i].TrimStart('\uFEFF').Trim();
                posicoes.TryAdd(nome, i);
            }

            int Pos(string nome) => posicoes.TryGetValue(nome, out var p)
                ? p
                : throw new InvalidDataException($"Coluna ausente no cabeçalho: {nome}");

            return new Colunas
            {
                NumFistel = Pos("NumFistel"),
                NumEstacao = Pos("NumEstacao"),
                SiglaUf = Pos("SiglaUf"),
                NumAto = Pos("NumAto"),
                CodMunicipio = Pos("CodMunicipio"),
                NomeMunicipio = Pos("Municipio.NomeMunicipio"),
                NomeEntidade = Pos("NomeEntidade"),
                EnderecoEstacao = Pos("EnderecoEstacao"),
                Latitude = Pos("Latitude"),
                Longitude = Pos("Longitude"),
                Tecnologia = Pos("Tecnologia"),
                DesignacaoEmissao = Pos("DesignacaoEmissao"),
                FreqTxMHz = Pos("FreqTxMHz"),
                FreqRxMHz = Pos("FreqRxMHz"),
                Azimute = Pos("Azimute"),
                ClasseEstacao = Pos("CodTipoClasseEstacao"),
                PotenciaTransmissorWatts = Pos("PotenciaTransmissorWatts"),
                GanhoAntena = Pos("GanhoAntena"),
                AlturaAntena = Pos("AlturaAntena"),
                AnguloElevacao = Pos("AnguloElevacao"),
                AnguloMeiaPotenciaAntena = Pos("AnguloMeiaPotenciaAntena"),
                FrenteCostaAntena = Pos("FrenteCostaAntena"),
                Polarizacao = Pos("Polarizacao"),
                CodTipoAntena = Pos("CodTipoAntena"),
                CodEquipamentoAntena = Pos("CodEquipamentoAntena"),
                CodEquipamentoTransmissor = Pos("CodEquipamentoTransmissor"),
                Status = Pos("Status.state"),
                DataLicenciamento = Pos("DataLicenciamento"),
                DataPrimeiroLicenciamento = Pos("DataPrimeiroLicenciamento"),
                DataValidade = Pos("DataValidade"),
            };
        }
    }
}

/// <summary>Inserção parametrizada reutilizável.</summary>
internal sealed class Insercao : IDisposable
{
    private readonly SqliteCommand _cmd;
    private readonly SqliteParameter[] _parametros;

    public Insercao(SqliteConnection conn, string tabela, params string[] colunas)
    {
        _cmd = conn.CreateCommand();
        _cmd.CommandText =
            $"INSERT INTO {tabela} ({string.Join(", ", colunas)}) " +
            $"VALUES ({string.Join(", ", colunas.Select(c => "@" + c))})";

        _parametros = new SqliteParameter[colunas.Length];
        for (int i = 0; i < colunas.Length; i++)
        {
            _parametros[i] = new SqliteParameter("@" + colunas[i], DBNull.Value);
            _cmd.Parameters.Add(_parametros[i]);
        }
    }

    public void DefinirTransacao(SqliteTransaction tx) => _cmd.Transaction = tx;

    /// <summary>Valores nulos (null) são gravados como NULL.</summary>
    public void Executar(params object?[] valores)
    {
        for (int i = 0; i < _parametros.Length; i++)
        {
            _parametros[i].Value = valores[i] ?? DBNull.Value;
        }
        _cmd.ExecuteNonQuery();
    }

    public void Dispose() => _cmd.Dispose();
}
