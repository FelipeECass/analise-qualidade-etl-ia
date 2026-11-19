namespace AnatelEtl.Models;

/// <summary>
/// Linha do CSV da Anatel exatamente como veio (somente texto, sem conversões).
/// Campos vazios no arquivo de origem são representados como <c>null</c>.
/// </summary>
public sealed record RawEmissao
{
    public string? Fistel { get; init; }
    public string? NumEstacao { get; init; }
    public string? NumAto { get; init; }
    public string? NomeEntidade { get; init; }
    public string? SiglaUf { get; init; }
    public string? CodMunicipio { get; init; }
    public string? Municipio { get; init; }
    public string? Endereco { get; init; }
    public string? DesignacaoEmissao { get; init; }
    public string? Tecnologia { get; init; }
    public string? FreqTxMhz { get; init; }
    public string? FreqRxMhz { get; init; }
    public string? Azimute { get; init; }
    public string? PotenciaTransmissorWatts { get; init; }
    public string? GanhoAntena { get; init; }
    public string? AlturaAntena { get; init; }
    public string? Latitude { get; init; }
    public string? Longitude { get; init; }
    public string? DataLicenciamento { get; init; }
    public string? DataPrimeiroLicenciamento { get; init; }
    public string? DataValidade { get; init; }
    public string? Status { get; init; }
    public string? ClasseEstacao { get; init; }
    public string? Polarizacao { get; init; }
    public string? AnguloElevacao { get; init; }
    public string? AnguloMeiaPotencia { get; init; }
    public string? FrenteCosta { get; init; }
    public string? CodTipoAntena { get; init; }
    public string? CodEquipamentoAntena { get; init; }
    public string? CodEquipamentoTransmissor { get; init; }
}
