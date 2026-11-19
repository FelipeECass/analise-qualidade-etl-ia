using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ClaudeHaikuZeroshotR2;

/// <summary>Pipeline ETL: lê files/&lt;UF&gt;.zip, limpa, deduplica e grava db/claude_haiku_zeroshot_r2.db.</summary>
internal sealed class Etl
{
    private static readonly string[] Ufs =
    {
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT",
        "PA", "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    };

    private static readonly string[] ColunasObrigatorias =
    {
        "Status.state", "NomeEntidade", "NumFistel", "NumAto", "NumEstacao", "EnderecoEstacao", "SiglaUf",
        "CodMunicipio", "Municipio.NomeMunicipio", "DesignacaoEmissao", "Tecnologia", "FreqTxMHz", "FreqRxMHz",
        "Azimute", "CodTipoClasseEstacao", "CodTipoAntena", "CodEquipamentoAntena", "GanhoAntena",
        "FrenteCostaAntena", "AnguloMeiaPotenciaAntena", "AnguloElevacao", "Polarizacao", "AlturaAntena",
        "CodEquipamentoTransmissor", "PotenciaTransmissorWatts", "Latitude", "Longitude",
        "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade",
    };

    private const string NomeBanco = "claude_haiku_zeroshot_r2.db";

    private readonly string _raiz;

    public Etl(string raiz) => _raiz = raiz;

    public string Executar()
    {
        var pastaFiles = Path.Combine(_raiz, "files");
        var pastaDb = Path.Combine(_raiz, "db");
        var dbPath = Path.Combine(pastaDb, NomeBanco);
        var ddlPath = Path.Combine(_raiz, "src", "DDL.sql");

        if (!File.Exists(ddlPath))
            throw new FileNotFoundException("DDL não encontrado.", ddlPath);

        foreach (var uf in Ufs)
        {
            var zip = Path.Combine(pastaFiles, uf + ".zip");
            if (!File.Exists(zip))
                throw new FileNotFoundException($"Arquivo da UF {uf} não encontrado.", zip);
        }

        Directory.CreateDirectory(pastaDb);
        RemoverBancoAnterior(dbPath);

        var ddl = File.ReadAllText(ddlPath, Encoding.UTF8);

        var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false };
        using var conn = new SqliteConnection(csb.ToString());
        conn.Open();

        using (var cmdDdl = conn.CreateCommand())
        {
            cmdDdl.CommandText = ddl;
            cmdDdl.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();

        var estado = new Estado
        {
            InsEstacao = new Insercao(conn, tx, "estacao",
                "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco",
                "latitude", "longitude", "coordenada_valida", "entidade", "operadora"),
            InsEmissao = new Insercao(conn, tx, "emissao",
                "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
                "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
                "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
                "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
                "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
                "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
                "elevacao_valida"),
        };

        foreach (var uf in Ufs)
        {
            ProcessarUf(Path.Combine(pastaFiles, uf + ".zip"), uf, estado);
        }

        tx.Commit();

        return $"Concluído. Linhas lidas: {estado.Lidas}; rejeitadas: {estado.Rejeitadas}; " +
               $"estações carregadas: {estado.Estacoes.Count}; emissões carregadas: {estado.Emissoes.Count}; " +
               $"emissões duplicadas não carregadas: {estado.Duplicadas}. Banco: {dbPath}";
    }

    private static void RemoverBancoAnterior(string dbPath)
    {
        foreach (var caminho in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal" })
        {
            if (File.Exists(caminho)) File.Delete(caminho);
        }
    }

    private static void ProcessarUf(string zipPath, string uf, Estado estado)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entrada = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Nenhum CSV dentro de {Path.GetFileName(zipPath)}.");

        using var leitor = new StreamReader(entrada.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var registros = Csv.Registros(leitor).GetEnumerator();

        if (!registros.MoveNext())
            throw new InvalidDataException($"CSV vazio em {Path.GetFileName(zipPath)}.");

        var cabecalho = registros.Current.Select(h => h.Trim()).ToArray();
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < cabecalho.Length; i++) idx.TryAdd(cabecalho[i], i);

        foreach (var coluna in ColunasObrigatorias)
        {
            if (!idx.ContainsKey(coluna))
                throw new InvalidDataException($"Coluna '{coluna}' ausente no CSV da UF {uf}.");
        }

        while (registros.MoveNext())
        {
            estado.Lidas++;
            RegistrarLinha(registros.Current, idx, estado);
        }
    }

    private static void RegistrarLinha(string[] campos, Dictionary<string, int> idx, Estado e)
    {
        string? G(string nome) => idx.TryGetValue(nome, out var i) && i < campos.Length ? campos[i] : null;

        var fistel = Limpeza.Texto(G("NumFistel"));
        var numEstacao = Limpeza.Texto(G("NumEstacao"));
        var uf = Limpeza.Maiusculas(G("SiglaUf"));
        if (fistel is null || numEstacao is null || uf is null)
        {
            e.Rejeitadas++;
            return;
        }

        var latitude = Limpeza.Numero(G("Latitude"));
        var longitude = Limpeza.Numero(G("Longitude"));

        // Estação: atributos vêm da primeira linha em que ela aparece.
        if (e.Estacoes.Add((uf, fistel, numEstacao)))
        {
            var entidade = Limpeza.TextoNormalizado(G("NomeEntidade"));
            e.InsEstacao.Executar(
                fistel,
                numEstacao,
                uf,
                Limpeza.Texto(G("CodMunicipio")),
                Limpeza.Texto(G("Municipio.NomeMunicipio")),
                Limpeza.TextoNormalizado(G("EnderecoEstacao")),
                Limpeza.ParaReal(latitude),
                Limpeza.ParaReal(longitude),
                Limpeza.CoordenadaValida(latitude, longitude) ? 1 : 0,
                entidade,
                Limpeza.Operadora(entidade));
        }

        // Emissão: deduplicação pela chave da seção 6.
        var numAto = Limpeza.Texto(G("NumAto"));
        var tecnologia = Limpeza.Maiusculas(G("Tecnologia"));
        var designacao = Limpeza.Maiusculas(G("DesignacaoEmissao"));
        var freqTx = Limpeza.Numero(G("FreqTxMHz"));
        var freqRx = Limpeza.Numero(G("FreqRxMHz"));
        var azimute = Limpeza.Numero(G("Azimute"));

        var chave = string.Join('\u001F',
            uf, fistel, numEstacao,
            Chave(numAto), Chave(azimute), Chave(freqTx), Chave(freqRx), Chave(designacao), Chave(tecnologia));
        if (!e.Emissoes.Add(chave))
        {
            e.Duplicadas++;
            return;
        }

        var largura = Limpeza.LarguraBandaKhz(designacao);
        decimal? duplex = freqTx is null || freqRx is null
            ? null
            : Math.Round(freqTx.Value - freqRx.Value, 3, MidpointRounding.AwayFromZero);

        var potencia = Limpeza.Numero(G("PotenciaTransmissorWatts"));
        var elevacao = Limpeza.Numero(G("AnguloElevacao"));
        var dataLic = Limpeza.Data(G("DataLicenciamento"));
        var dataPrim = Limpeza.Data(G("DataPrimeiroLicenciamento"));
        var dataVal = Limpeza.Data(G("DataValidade"));

        var vencida = dataVal is { } v && v < Limpeza.Hoje;
        decimal? anos = dataPrim is { } p
            ? Math.Round((decimal)(Limpeza.Hoje.DayNumber - p.DayNumber) / 365.25m, 1, MidpointRounding.AwayFromZero)
            : null;

        bool? azimuteValido = azimute is null ? null : azimute >= 0 && azimute <= 360;
        bool? elevacaoValida = elevacao is null ? null : elevacao >= -90 && elevacao <= 90;

        e.InsEmissao.Executar(
            uf,
            fistel,
            numEstacao,
            numAto,
            tecnologia,
            Limpeza.Geracao(tecnologia),
            designacao,
            Limpeza.ParaReal(largura),
            Limpeza.ParaReal(freqTx),
            Limpeza.ParaReal(freqRx),
            Limpeza.ParaReal(duplex),
            Limpeza.ParaReal(azimute),
            Limpeza.ParaReal(potencia),
            Limpeza.ParaReal(Limpeza.Numero(G("GanhoAntena"))),
            Limpeza.ParaReal(Limpeza.Numero(G("AlturaAntena"))),
            Data(dataLic),
            Data(dataPrim),
            Data(dataVal),
            vencida ? 1 : 0,
            Limpeza.ParaReal(anos),
            Limpeza.Texto(G("Status.state")),
            Limpeza.Texto(G("CodTipoClasseEstacao")),
            Limpeza.Maiusculas(G("Polarizacao")),
            Limpeza.ParaReal(elevacao),
            Limpeza.ParaReal(Limpeza.Numero(G("AnguloMeiaPotenciaAntena"))),
            Limpeza.ParaReal(Limpeza.Numero(G("FrenteCostaAntena"))),
            Limpeza.Texto(G("CodTipoAntena")),
            Limpeza.Texto(G("CodEquipamentoAntena")),
            Limpeza.Texto(G("CodEquipamentoTransmissor")),
            Limpeza.PotenciaDbm(potencia),
            Bit(azimuteValido),
            Bit(elevacaoValida));
    }

    private static string Chave(string? valor) => valor ?? "\u0000";

    private static string Chave(decimal? valor) =>
        valor is null ? "\u0000" : valor.Value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string? Data(DateOnly? data) => data?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int? Bit(bool? valor) => valor is null ? null : valor.Value ? 1 : 0;

    private sealed class Estado
    {
        public long Lidas;
        public long Rejeitadas;
        public long Duplicadas;
        public readonly HashSet<(string Uf, string Fistel, string NumEstacao)> Estacoes = new();
        public readonly HashSet<string> Emissoes = new(StringComparer.Ordinal);
        public required Insercao InsEstacao { get; init; }
        public required Insercao InsEmissao { get; init; }
    }

    /// <summary>Comando INSERT preparado uma vez e reutilizado para cada linha.</summary>
    private sealed class Insercao : IDisposable
    {
        private readonly SqliteCommand _cmd;

        public Insercao(SqliteConnection conn, SqliteTransaction tx, string tabela, params string[] colunas)
        {
            var sql = $"INSERT INTO {tabela} ({string.Join(", ", colunas)}) " +
                      $"VALUES ({string.Join(", ", colunas.Select(c => "$" + c))})";
            _cmd = conn.CreateCommand();
            _cmd.Transaction = tx;
            _cmd.CommandText = sql;
            foreach (var c in colunas)
            {
                _cmd.Parameters.AddWithValue("$" + c, DBNull.Value);
            }
            _cmd.Prepare();
        }

        public void Executar(params object?[] valores)
        {
            for (var i = 0; i < valores.Length; i++)
            {
                _cmd.Parameters[i].Value = valores[i] ?? DBNull.Value;
            }
            _cmd.ExecuteNonQuery();
        }

        public void Dispose() => _cmd.Dispose();
    }
}
