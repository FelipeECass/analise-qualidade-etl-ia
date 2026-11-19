using System.IO.Compression;
using System.Text;
using claude_haiku_completo_r3.Csv;
using claude_haiku_completo_r3.Persistencia;
using claude_haiku_completo_r3.Regras;
using static claude_haiku_completo_r3.Limpeza.Limpeza;

namespace claude_haiku_completo_r3.Pipeline;

/// <summary>
/// Fluxo ZIP → linha → limpeza → rejeição → estação (primeira ocorrência) → emissão
/// (deduplicada pela primeira ocorrência) → banco.
/// </summary>
public static class EtlPipeline
{
    private const char Sep = '\u001F';
    private const string Nulo = "\u0000";

    public static string Executar(Rotas rotas)
    {
        if (!Directory.Exists(rotas.PastaZips))
            throw new DirectoryNotFoundException($"Pasta de ZIPs não encontrada: {rotas.PastaZips}");

        var zips = Directory.GetFiles(rotas.PastaZips, "*.zip")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        if (zips.Length == 0)
            throw new InvalidOperationException($"Nenhum ZIP encontrado em {rotas.PastaZips}.");

        var totais = new Sumario();
        var ufsConcluidas = new HashSet<string>(StringComparer.Ordinal);

        using var banco = BancoSqlite.Criar(rotas.Banco, rotas.Ddl);

        foreach (var zip in zips)
        {
            var sumarioArquivo = ProcessarArquivo(zip, banco, ufsConcluidas);
            totais.Somar(sumarioArquivo);
            Console.WriteLine($"{Path.GetFileName(zip)}: {sumarioArquivo}");
        }

        return $"CONCLUÍDO. Arquivos: {zips.Length}. {totais}";
    }

    private static Sumario ProcessarArquivo(string caminhoZip, BancoSqlite banco, HashSet<string> ufsConcluidas)
    {
        using var zip = ZipFile.OpenRead(caminhoZip);
        if (zip.Entries.Count != 1)
            throw new InvalidDataException($"{caminhoZip}: esperado 1 arquivo no ZIP, encontrados {zip.Entries.Count}.");

        var sumario = new Sumario();
        var estacoesVistas = new HashSet<string>(StringComparer.Ordinal);
        var emissoesVistas = new HashSet<string>(StringComparer.Ordinal);
        var ufsDesteArquivo = new HashSet<string>(StringComparer.Ordinal);

        using var entrada = zip.Entries[0].Open();
        using var reader = new StreamReader(entrada, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader);

        var cabecalho = csv.LerRegistro()
            ?? throw new InvalidDataException($"{caminhoZip}: arquivo sem cabeçalho.");
        var colunas = Colunas.Mapear(cabecalho, caminhoZip);

        banco.IniciarTransacao();
        long numeroLinha = 1;
        string[]? registro;
        while ((registro = csv.LerRegistro()) is not null)
        {
            numeroLinha++;
            if (registro.Length != cabecalho.Length)
                throw new InvalidDataException(
                    $"{caminhoZip}, registro {numeroLinha}: {registro.Length} campos, esperado {cabecalho.Length}.");

            sumario.LinhasLidas++;
            var linha = new Linha(registro, colunas);

            var fistel = Texto(linha.Bruto("NumFistel"));
            var numEstacao = Texto(linha.Bruto("NumEstacao"));
            var uf = Maiusculas(linha.Bruto("SiglaUf"));

            if (fistel is null || numEstacao is null || uf is null)
            {
                sumario.LinhasRejeitadas++;
                continue;
            }

            if (ufsConcluidas.Contains(uf))
                throw new InvalidDataException($"UF {uf} aparece em mais de um ZIP ({caminhoZip}). A deduplicação assume uma UF por arquivo.");
            ufsDesteArquivo.Add(uf);

            var chaveEstacao = string.Join(Sep, uf, fistel, numEstacao);
            var entidade = TextoNormalizado(linha.Bruto("NomeEntidade"));
            var operadora = Derivadas.Operadora(entidade);
            var latitude = Numero(linha.Bruto("Latitude"));
            var longitude = Numero(linha.Bruto("Longitude"));
            var coordValida = Derivadas.CoordenadaValida(latitude, longitude) ? 1 : 0;

            if (estacoesVistas.Add(chaveEstacao))
            {
                banco.InserirEstacao(
                [
                    fistel,
                    numEstacao,
                    uf,
                    Texto(linha.Bruto("CodMunicipio")),
                    Texto(linha.Bruto("Municipio.NomeMunicipio")),
                    TextoNormalizado(linha.Bruto("EnderecoEstacao")),
                    latitude,
                    longitude,
                    coordValida,
                    entidade,
                    operadora,
                ]);
                sumario.EstacoesInseridas++;
            }

            var emissao = MontarEmissao(linha, uf, fistel, numEstacao);

            if (!emissoesVistas.Add(ChaveEmissao(emissao)))
            {
                sumario.EmissoesDuplicadas++;
                continue;
            }

            banco.InserirEmissao(emissao.ParaBanco());
            sumario.EmissoesInseridas++;
        }

        banco.ConfirmarTransacao();

        foreach (var u in ufsDesteArquivo) ufsConcluidas.Add(u);
        return sumario;
    }

    /// <summary>Monta a emissão com todas as colunas tratadas, na ordem do DDL.</summary>
    private static Emissao MontarEmissao(Linha linha, string uf, string fistel, string numEstacao)
    {
        var tecnologia = Maiusculas(linha.Bruto("Tecnologia"));
        var designacao = Maiusculas(linha.Bruto("DesignacaoEmissao"));
        var freqTx = Numero(linha.Bruto("FreqTxMHz"));
        var freqRx = Numero(linha.Bruto("FreqRxMHz"));
        var freqTxDecimal = NumeroDecimal(linha.Bruto("FreqTxMHz"));
        var freqRxDecimal = NumeroDecimal(linha.Bruto("FreqRxMHz"));
        var azimute = Numero(linha.Bruto("Azimute"));
        var potencia = Numero(linha.Bruto("PotenciaTransmissorWatts"));
        var dataLic = Data(linha.Bruto("DataLicenciamento"));
        var dataPrimeiro = Data(linha.Bruto("DataPrimeiroLicenciamento"));
        var dataValidade = Data(linha.Bruto("DataValidade"));
        var elevacao = Numero(linha.Bruto("AnguloElevacao"));

        return new Emissao
        {
            Uf = uf,
            Fistel = fistel,
            NumEstacao = numEstacao,
            NumAto = Texto(linha.Bruto("NumAto")),
            Tecnologia = tecnologia,
            Geracao = Derivadas.Geracao(tecnologia),
            DesignacaoEmissao = designacao,
            LarguraBandaKhz = Derivadas.LarguraBandaKhz(designacao),
            FreqTxMhz = freqTx,
            FreqRxMhz = freqRx,
            DuplexMhz = Derivadas.Duplex(freqTxDecimal, freqRxDecimal),
            Azimute = azimute,
            PotenciaW = potencia,
            GanhoAntena = Numero(linha.Bruto("GanhoAntena")),
            AlturaAntenaM = Numero(linha.Bruto("AlturaAntena")),
            DataLicenciamento = Banco(dataLic),
            DataPrimeiroLicenciamento = Banco(dataPrimeiro),
            DataValidade = Banco(dataValidade),
            Vencida = Derivadas.Vencida(dataValidade),
            AnosLicenciada = Derivadas.AnosLicenciada(dataPrimeiro),
            Status = Texto(linha.Bruto("Status.state")),
            ClasseEstacao = Texto(linha.Bruto("CodTipoClasseEstacao")),
            Polarizacao = Maiusculas(linha.Bruto("Polarizacao")),
            AnguloElevacaoGraus = elevacao,
            AnguloMeiaPotenciaGraus = Numero(linha.Bruto("AnguloMeiaPotenciaAntena")),
            FrenteCostaDb = Numero(linha.Bruto("FrenteCostaAntena")),
            CodTipoAntena = Texto(linha.Bruto("CodTipoAntena")),
            CodEquipamentoAntena = Texto(linha.Bruto("CodEquipamentoAntena")),
            CodEquipamentoTransmissor = Texto(linha.Bruto("CodEquipamentoTransmissor")),
            PotenciaDbm = Derivadas.PotenciaDbm(potencia),
            AzimuteValido = Derivadas.AzimuteValido(azimute),
            ElevacaoValida = Derivadas.ElevacaoValida(elevacao),
        };
    }

    /// <summary>Chave de duplicidade com os valores já tratados; NULL é igual a NULL.</summary>
    private static string ChaveEmissao(Emissao e) => string.Join(Sep,
        e.Uf, e.Fistel, e.NumEstacao,
        e.NumAto ?? Nulo,
        ParteChave(e.Azimute),
        ParteChave(e.FreqTxMhz),
        ParteChave(e.FreqRxMhz),
        e.DesignacaoEmissao ?? Nulo,
        e.Tecnologia ?? Nulo);

    private static string ParteChave(double? v) =>
        v?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? Nulo;

    private static string? Banco(DateOnly? data) => BancoSqlite.FormatarData(data);
}

/// <summary>Índices das colunas do arquivo bruto, localizados pelo nome do cabeçalho.</summary>
internal sealed class Colunas
{
    private readonly Dictionary<string, int> _indices;

    private Colunas(Dictionary<string, int> indices) => _indices = indices;

    public static readonly string[] Obrigatorias =
    [
        "Status.state", "NomeEntidade", "NumFistel", "NumAto", "NumEstacao", "EnderecoEstacao",
        "SiglaUf", "CodMunicipio", "DesignacaoEmissao", "Tecnologia", "FreqTxMHz", "FreqRxMHz",
        "Azimute", "CodTipoClasseEstacao", "CodTipoAntena", "CodEquipamentoAntena", "GanhoAntena",
        "FrenteCostaAntena", "AnguloMeiaPotenciaAntena", "AnguloElevacao", "Polarizacao",
        "AlturaAntena", "CodEquipamentoTransmissor", "PotenciaTransmissorWatts", "Latitude",
        "Longitude", "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade",
        "Municipio.NomeMunicipio",
    ];

    public static Colunas Mapear(string[] cabecalho, string origem)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < cabecalho.Length; i++)
            indices[cabecalho[i].Trim()] = i;

        var faltando = Obrigatorias.Where(c => !indices.ContainsKey(c)).ToArray();
        if (faltando.Length > 0)
            throw new InvalidDataException($"{origem}: colunas ausentes no cabeçalho: {string.Join(", ", faltando)}");

        return new Colunas(indices);
    }

    public int Indice(string nome) => _indices[nome];
}

/// <summary>Registro bruto com acesso por nome de coluna. Valores brutos ainda não limpos.</summary>
internal readonly struct Linha
{
    private readonly string[] _campos;
    private readonly Colunas _colunas;

    public Linha(string[] campos, Colunas colunas)
    {
        _campos = campos;
        _colunas = colunas;
    }

    public string? Bruto(string nome)
    {
        var i = _colunas.Indice(nome);
        return i < _campos.Length ? _campos[i] : null;
    }
}

/// <summary>Valores de uma linha de emissão já tratados (espelha a tabela emissao).</summary>
internal sealed class Emissao
{
    public required string Uf { get; init; }
    public required string Fistel { get; init; }
    public required string NumEstacao { get; init; }
    public string? NumAto { get; init; }
    public string? Tecnologia { get; init; }
    public required string Geracao { get; init; }
    public string? DesignacaoEmissao { get; init; }
    public double? LarguraBandaKhz { get; init; }
    public double? FreqTxMhz { get; init; }
    public double? FreqRxMhz { get; init; }
    public double? DuplexMhz { get; init; }
    public double? Azimute { get; init; }
    public double? PotenciaW { get; init; }
    public double? GanhoAntena { get; init; }
    public double? AlturaAntenaM { get; init; }
    public string? DataLicenciamento { get; init; }
    public string? DataPrimeiroLicenciamento { get; init; }
    public string? DataValidade { get; init; }
    public int Vencida { get; init; }
    public double? AnosLicenciada { get; init; }
    public string? Status { get; init; }
    public string? ClasseEstacao { get; init; }
    public string? Polarizacao { get; init; }
    public double? AnguloElevacaoGraus { get; init; }
    public double? AnguloMeiaPotenciaGraus { get; init; }
    public double? FrenteCostaDb { get; init; }
    public string? CodTipoAntena { get; init; }
    public string? CodEquipamentoAntena { get; init; }
    public string? CodEquipamentoTransmissor { get; init; }
    public double? PotenciaDbm { get; init; }
    public int? AzimuteValido { get; init; }
    public int? ElevacaoValida { get; init; }

    /// <summary>Valores na ordem das colunas de ColunasEmissao em BancoSqlite.</summary>
    public IReadOnlyList<object?> ParaBanco() =>
    [
        Uf, Fistel, NumEstacao, NumAto, Tecnologia, Geracao, DesignacaoEmissao,
        LarguraBandaKhz, FreqTxMhz, FreqRxMhz, DuplexMhz, Azimute, PotenciaW,
        GanhoAntena, AlturaAntenaM, DataLicenciamento, DataPrimeiroLicenciamento,
        DataValidade, Vencida, AnosLicenciada, Status, ClasseEstacao, Polarizacao,
        AnguloElevacaoGraus, AnguloMeiaPotenciaGraus, FrenteCostaDb, CodTipoAntena,
        CodEquipamentoAntena, CodEquipamentoTransmissor, PotenciaDbm, AzimuteValido,
        ElevacaoValida,
    ];
}

/// <summary>Contadores de execução.</summary>
internal sealed class Sumario
{
    public long LinhasLidas;
    public long LinhasRejeitadas;
    public long EstacoesInseridas;
    public long EmissoesInseridas;
    public long EmissoesDuplicadas;

    public void Somar(Sumario outro)
    {
        LinhasLidas += outro.LinhasLidas;
        LinhasRejeitadas += outro.LinhasRejeitadas;
        EstacoesInseridas += outro.EstacoesInseridas;
        EmissoesInseridas += outro.EmissoesInseridas;
        EmissoesDuplicadas += outro.EmissoesDuplicadas;
    }

    public override string ToString() =>
        $"lidas={LinhasLidas} rejeitadas={LinhasRejeitadas} estacoes={EstacoesInseridas} " +
        $"emissoes={EmissoesInseridas} duplicadas={EmissoesDuplicadas}";
}
