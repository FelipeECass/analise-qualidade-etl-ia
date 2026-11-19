using System.Globalization;

namespace Etl.Transform;

/// <summary>Estação (uma por uf + fistel + num_estacao). Colunas na ordem do DDL.</summary>
public sealed record Station(
    string Fistel,
    string NumEstacao,
    string Uf,
    string? CodMunicipio,
    string? Municipio,
    string? Endereco,
    double? Latitude,
    double? Longitude,
    int CoordenadaValida,
    string? Entidade,
    string Operadora)
{
    public static readonly string[] Columns =
    {
        "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco",
        "latitude", "longitude", "coordenada_valida", "entidade", "operadora",
    };

    public object?[] Values() =>
    [
        Fistel, NumEstacao, Uf, CodMunicipio, Municipio, Endereco,
        Latitude, Longitude, CoordenadaValida, Entidade, Operadora,
    ];
}

/// <summary>Chave de duplicidade de emissão (seção 6), com valores já tratados.</summary>
public readonly record struct EmissionKey(
    string Uf,
    string Fistel,
    string NumEstacao,
    string? NumAto,
    double? Azimute,
    double? FreqTxMhz,
    double? FreqRxMhz,
    string? DesignacaoEmissao,
    string? Tecnologia);

/// <summary>Emissão. Colunas na ordem do DDL (sem id).</summary>
public sealed record Emission(
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
    string? DataLicenciamento,
    string? DataPrimeiroLicenciamento,
    string? DataValidade,
    int Vencida,
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
    int? ElevacaoValida)
{
    public static readonly string[] Columns =
    {
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
        "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
        "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
        "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
        "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
        "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
        "elevacao_valida",
    };

    public EmissionKey Key => new(Uf, Fistel, NumEstacao, NumAto, Azimute, FreqTxMhz, FreqRxMhz,
        DesignacaoEmissao, Tecnologia);

    public object?[] Values() =>
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

/// <summary>Resultado do mapeamento de uma linha aceita.</summary>
public sealed record MappedRow(Station Station, Emission Emission);

/// <summary>Índices das colunas brutas, resolvidos pelo nome do cabeçalho.</summary>
public sealed record ColumnMap(
    int StatusState,
    int NomeEntidade,
    int NumFistel,
    int NumEstacao,
    int NumAto,
    int EnderecoEstacao,
    int SiglaUf,
    int CodMunicipio,
    int DesignacaoEmissao,
    int Tecnologia,
    int FreqTxMhz,
    int FreqRxMhz,
    int Azimute,
    int CodTipoClasseEstacao,
    int CodTipoAntena,
    int CodEquipamentoAntena,
    int GanhoAntena,
    int FrenteCostaAntena,
    int AnguloMeiaPotenciaAntena,
    int AnguloElevacao,
    int Polarizacao,
    int AlturaAntena,
    int CodEquipamentoTransmissor,
    int PotenciaTransmissorWatts,
    int Latitude,
    int Longitude,
    int DataLicenciamento,
    int DataPrimeiroLicenciamento,
    int DataValidade,
    int NomeMunicipio)
{
    public static ColumnMap FromHeader(string[] header)
    {
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim().TrimStart('\uFEFF').Trim();
            idx.TryAdd(name, i);
        }

        var faltando = new List<string>();
        int F(string name)
        {
            if (idx.TryGetValue(name, out var i)) return i;
            faltando.Add(name);
            return -1;
        }

        var map = new ColumnMap(
            F("Status.state"), F("NomeEntidade"), F("NumFistel"), F("NumEstacao"), F("NumAto"),
            F("EnderecoEstacao"), F("SiglaUf"), F("CodMunicipio"), F("DesignacaoEmissao"),
            F("Tecnologia"), F("FreqTxMHz"), F("FreqRxMHz"), F("Azimute"), F("CodTipoClasseEstacao"),
            F("CodTipoAntena"), F("CodEquipamentoAntena"), F("GanhoAntena"), F("FrenteCostaAntena"),
            F("AnguloMeiaPotenciaAntena"), F("AnguloElevacao"), F("Polarizacao"), F("AlturaAntena"),
            F("CodEquipamentoTransmissor"), F("PotenciaTransmissorWatts"), F("Latitude"), F("Longitude"),
            F("DataLicenciamento"), F("DataPrimeiroLicenciamento"), F("DataValidade"),
            F("Municipio.NomeMunicipio"));

        if (faltando.Count > 0)
        {
            throw new InvalidDataException("Colunas obrigatórias ausentes no cabeçalho: " + string.Join(", ", faltando));
        }
        return map;
    }
}

/// <summary>Transforma uma linha bruta em estação + emissão (seções 1 a 5), ou null se rejeitada.</summary>
public static class RowMapper
{
    public static MappedRow? Map(ColumnMap c, string?[] f)
    {
        var uf = Clean.Upper(Get(f, c.SiglaUf));
        var fistel = Clean.Text(Get(f, c.NumFistel));
        var numEstacao = Clean.Text(Get(f, c.NumEstacao));
        if (uf is null || fistel is null || numEstacao is null) return null;

        // Estação
        var entidade = Clean.Normalize(Get(f, c.NomeEntidade));
        var latitude = Clean.Number(Get(f, c.Latitude));
        var longitude = Clean.Number(Get(f, c.Longitude));
        var station = new Station(
            Fistel: fistel,
            NumEstacao: numEstacao,
            Uf: uf,
            CodMunicipio: Clean.Text(Get(f, c.CodMunicipio)),
            Municipio: Clean.Text(Get(f, c.NomeMunicipio)),
            Endereco: Clean.Normalize(Get(f, c.EnderecoEstacao)),
            Latitude: latitude,
            Longitude: longitude,
            CoordenadaValida: Derive.CoordenadaValida(latitude, longitude),
            Entidade: entidade,
            Operadora: Derive.Operadora(entidade));

        // Emissão
        var tecnologia = Clean.Upper(Get(f, c.Tecnologia));
        var designacao = Clean.Upper(Get(f, c.DesignacaoEmissao));
        var freqTx = Clean.Number(Get(f, c.FreqTxMhz));
        var freqRx = Clean.Number(Get(f, c.FreqRxMhz));
        var freqTxDec = Clean.Decimal(Get(f, c.FreqTxMhz));
        var freqRxDec = Clean.Decimal(Get(f, c.FreqRxMhz));
        var potenciaW = Clean.Number(Get(f, c.PotenciaTransmissorWatts));
        var azimute = Clean.Number(Get(f, c.Azimute));
        var licenciamento = Clean.Date(Get(f, c.DataLicenciamento));
        var primeiro = Clean.Date(Get(f, c.DataPrimeiroLicenciamento));
        var validade = Clean.Date(Get(f, c.DataValidade));
        var vencida = Derive.Vencida(validade);
        var azimuteValido = Derive.AzimuteValido(azimute);
        var elevacao = Clean.Number(Get(f, c.AnguloElevacao));

        var emission = new Emission(
            Uf: uf,
            Fistel: fistel,
            NumEstacao: numEstacao,
            NumAto: Clean.Text(Get(f, c.NumAto)),
            Tecnologia: tecnologia,
            Geracao: Derive.Geracao(tecnologia),
            DesignacaoEmissao: designacao,
            LarguraBandaKhz: Derive.LarguraBandaKhz(designacao),
            FreqTxMhz: freqTx,
            FreqRxMhz: freqRx,
            DuplexMhz: Derive.DuplexMhz(freqTxDec, freqRxDec),
            Azimute: azimute,
            PotenciaW: potenciaW,
            GanhoAntena: Clean.Number(Get(f, c.GanhoAntena)),
            AlturaAntenaM: Clean.Number(Get(f, c.AlturaAntena)),
            DataLicenciamento: Format(licenciamento),
            DataPrimeiroLicenciamento: Format(primeiro),
            DataValidade: Format(validade),
            Vencida: vencida,
            AnosLicenciada: Derive.AnosLicenciada(primeiro),
            Status: Clean.Text(Get(f, c.StatusState)),
            ClasseEstacao: Clean.Text(Get(f, c.CodTipoClasseEstacao)),
            Polarizacao: Clean.Upper(Get(f, c.Polarizacao)),
            AnguloElevacaoGraus: elevacao,
            AnguloMeiaPotenciaGraus: Clean.Number(Get(f, c.AnguloMeiaPotenciaAntena)),
            FrenteCostaDb: Clean.Number(Get(f, c.FrenteCostaAntena)),
            CodTipoAntena: Clean.Text(Get(f, c.CodTipoAntena)),
            CodEquipamentoAntena: Clean.Text(Get(f, c.CodEquipamentoAntena)),
            CodEquipamentoTransmissor: Clean.Text(Get(f, c.CodEquipamentoTransmissor)),
            PotenciaDbm: Derive.PotenciaDbm(potenciaW),
            AzimuteValido: azimuteValido,
            ElevacaoValida: Derive.ElevacaoValida(elevacao));

        return new MappedRow(station, emission);
    }

    private static string? Get(string?[] f, int idx) => idx >= 0 && idx < f.Length ? f[idx] : null;

    private static string? Format(DateOnly? d) =>
        d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
