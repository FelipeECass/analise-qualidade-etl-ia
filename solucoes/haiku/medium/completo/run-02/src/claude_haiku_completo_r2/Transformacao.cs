namespace ClaudeHaikuCompletoR2;

/// <summary>Estação já tratada, pronta para gravação.</summary>
internal sealed record Estacao(
    string Fistel,
    string NumEstacao,
    string Uf,
    string? CodMunicipio,
    string? Municipio,
    string? Endereco,
    double? Latitude,
    double? Longitude,
    bool CoordenadaValida,
    string? Entidade,
    string Operadora);

/// <summary>Emissão já tratada, pronta para gravação.</summary>
internal sealed record Emissao(
    string Uf,
    string Fistel,
    string NumEstacao,
    string? NumAto,
    string? Tecnologia,
    string Geracao,
    string? DesignacaoEmissao,
    double? LarguraBandaKhz,
    double? FreqTxMhz,
    double? FreqRxMhz,
    double? DuplexMhz,
    double? Azimute,
    double? PotenciaW,
    double? GanhoAntena,
    double? AlturaAntenaM,
    DateOnly? DataLicenciamento,
    DateOnly? DataPrimeiroLicenciamento,
    DateOnly? DataValidade,
    bool Vencida,
    double? AnosLicenciada,
    string? Status,
    string? ClasseEstacao,
    string? Polarizacao,
    double? AnguloElevacaoGraus,
    double? AnguloMeiaPotenciaGraus,
    double? FrenteCostaDb,
    string? CodTipoAntena,
    string? CodEquipamentoAntena,
    string? CodEquipamentoTransmissor,
    double? PotenciaDbm,
    int? AzimuteValido,
    int? ElevacaoValida);

/// <summary>Uma linha do arquivo bruto transformada em estação e emissão.</summary>
internal sealed record LinhaTratada(Estacao Estacao, Emissao Emissao);

/// <summary>
/// Converte uma linha bruta (campos já separados) em <see cref="LinhaTratada"/>.
/// Colunas são localizadas pelo nome do cabeçalho, não pela posição.
/// </summary>
internal sealed class Transformacao
{
    private static readonly string[] ColunasObrigatorias =
    [
        "Status.state", "NomeEntidade", "NumFistel", "NumEstacao", "EnderecoEstacao", "SiglaUf",
        "CodMunicipio", "Municipio.NomeMunicipio", "DesignacaoEmissao", "Tecnologia", "NumAto",
        "FreqTxMHz", "FreqRxMHz", "Azimute", "CodTipoClasseEstacao", "CodTipoAntena",
        "CodEquipamentoAntena", "GanhoAntena", "FrenteCostaAntena", "AnguloMeiaPotenciaAntena",
        "AnguloElevacao", "Polarizacao", "AlturaAntena", "CodEquipamentoTransmissor",
        "PotenciaTransmissorWatts", "Latitude", "Longitude", "DataLicenciamento",
        "DataPrimeiroLicenciamento", "DataValidade",
    ];

    private readonly Dictionary<string, int> _indices;

    public Transformacao(string[] cabecalho)
    {
        _indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < cabecalho.Length; i++)
        {
            var nome = cabecalho[i].Trim();
            _indices.TryAdd(nome, i);
        }

        var ausentes = ColunasObrigatorias.Where(c => !_indices.ContainsKey(c)).ToArray();
        if (ausentes.Length > 0)
            throw new InvalidDataException("Colunas ausentes no cabeçalho: " + string.Join(", ", ausentes));
    }

    /// <summary>Trata a linha; devolve null se ela deve ser rejeitada (seção 2).</summary>
    public LinhaTratada? Transformar(string[] campos)
    {
        string? Bruto(string nome)
        {
            var i = _indices[nome];
            return i < campos.Length ? campos[i] : null;
        }

        var fistel = Limpeza.Texto(Bruto("NumFistel"));
        var numEstacao = Limpeza.Texto(Bruto("NumEstacao"));
        var uf = Limpeza.Maiusculas(Bruto("SiglaUf"));
        if (fistel is null || numEstacao is null || uf is null) return null;

        var entidade = Limpeza.TextoNormalizado(Bruto("NomeEntidade"));
        var latitude = Limpeza.Numero(Bruto("Latitude"));
        var longitude = Limpeza.Numero(Bruto("Longitude"));

        var estacao = new Estacao(
            Fistel: fistel,
            NumEstacao: numEstacao,
            Uf: uf,
            CodMunicipio: Limpeza.Texto(Bruto("CodMunicipio")),
            Municipio: Limpeza.Texto(Bruto("Municipio.NomeMunicipio")),
            Endereco: Limpeza.TextoNormalizado(Bruto("EnderecoEstacao")),
            Latitude: latitude,
            Longitude: longitude,
            CoordenadaValida: Derivados.CoordenadaValida(latitude, longitude),
            Entidade: entidade,
            Operadora: Derivados.Operadora(entidade));

        var tecnologia = Limpeza.Maiusculas(Bruto("Tecnologia"));
        var designacao = Limpeza.Maiusculas(Bruto("DesignacaoEmissao"));
        var freqTx = Limpeza.Numero(Bruto("FreqTxMHz"));
        var freqRx = Limpeza.Numero(Bruto("FreqRxMHz"));
        var azimute = Limpeza.Numero(Bruto("Azimute"));
        var potenciaW = Limpeza.Numero(Bruto("PotenciaTransmissorWatts"));
        var anguloElevacao = Limpeza.Numero(Bruto("AnguloElevacao"));
        var dataLicenciamento = Limpeza.Data(Bruto("DataLicenciamento"));
        var dataPrimeiro = Limpeza.Data(Bruto("DataPrimeiroLicenciamento"));
        var dataValidade = Limpeza.Data(Bruto("DataValidade"));

        var emissao = new Emissao(
            Uf: uf,
            Fistel: fistel,
            NumEstacao: numEstacao,
            NumAto: Limpeza.Texto(Bruto("NumAto")),
            Tecnologia: tecnologia,
            Geracao: Derivados.Geracao(tecnologia),
            DesignacaoEmissao: designacao,
            LarguraBandaKhz: Derivados.LarguraBandaKhz(designacao),
            FreqTxMhz: freqTx,
            FreqRxMhz: freqRx,
            DuplexMhz: Derivados.Duplex(freqTx, freqRx),
            Azimute: azimute,
            PotenciaW: potenciaW,
            GanhoAntena: Limpeza.Numero(Bruto("GanhoAntena")),
            AlturaAntenaM: Limpeza.Numero(Bruto("AlturaAntena")),
            DataLicenciamento: dataLicenciamento,
            DataPrimeiroLicenciamento: dataPrimeiro,
            DataValidade: dataValidade,
            Vencida: Derivados.Vencida(dataValidade),
            AnosLicenciada: Derivados.AnosLicenciada(dataPrimeiro),
            Status: Limpeza.Texto(Bruto("Status.state")),
            ClasseEstacao: Limpeza.Texto(Bruto("CodTipoClasseEstacao")),
            Polarizacao: Limpeza.Maiusculas(Bruto("Polarizacao")),
            AnguloElevacaoGraus: anguloElevacao,
            AnguloMeiaPotenciaGraus: Limpeza.Numero(Bruto("AnguloMeiaPotenciaAntena")),
            FrenteCostaDb: Limpeza.Numero(Bruto("FrenteCostaAntena")),
            CodTipoAntena: Limpeza.Texto(Bruto("CodTipoAntena")),
            CodEquipamentoAntena: Limpeza.Texto(Bruto("CodEquipamentoAntena")),
            CodEquipamentoTransmissor: Limpeza.Texto(Bruto("CodEquipamentoTransmissor")),
            PotenciaDbm: Derivados.PotenciaDbm(potenciaW),
            AzimuteValido: Derivados.AzimuteValido(azimute),
            ElevacaoValida: Derivados.ElevacaoValida(anguloElevacao));

        return new LinhaTratada(estacao, emissao);
    }
}
