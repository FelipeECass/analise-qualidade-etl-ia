namespace AnatelEtl.Models;

/// <summary>Uma emissão (setor/frequência) de uma estação, já tipada e enriquecida.</summary>
public sealed record Emissao(
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
    string FaixaFrequencia,
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
    bool? AzimuteValido,
    bool? ElevacaoValida,
    bool? DuplexCoerente)
{
    /// <summary>Chave natural usada para eliminar duplicatas exatas.</summary>
    public EmissaoKey Key => new(
        Uf, Fistel, NumEstacao, NumAto, Azimute, FreqTxMhz, FreqRxMhz, DesignacaoEmissao, Tecnologia);
}

public readonly record struct EmissaoKey(
    string Uf,
    string Fistel,
    string NumEstacao,
    string? NumAto,
    double? Azimute,
    double? FreqTxMhz,
    double? FreqRxMhz,
    string? DesignacaoEmissao,
    string? Tecnologia);
