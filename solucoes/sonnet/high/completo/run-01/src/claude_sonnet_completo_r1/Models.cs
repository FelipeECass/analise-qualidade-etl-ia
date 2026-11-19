namespace AnatelEtl;

public readonly record struct StationKey(string Uf, string Fistel, string NumEstacao);

public readonly record struct EmissionKey(
    string Uf, string Fistel, string NumEstacao, string? NumAto,
    double? Azimute, double? FreqTx, double? FreqRx, string? Designacao, string? Tecnologia);

public sealed class Estacao
{
    public required string Fistel { get; init; }
    public required string NumEstacao { get; init; }
    public required string Uf { get; init; }
    public string? CodMunicipio { get; init; }
    public string? Municipio { get; init; }
    public string? Endereco { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public bool CoordenadaValida { get; init; }
    public string? Entidade { get; init; }
    public required string Operadora { get; init; }
}

public sealed class Emissao
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
    public DateOnly? DataLicenciamento { get; init; }
    public DateOnly? DataPrimeiroLicenciamento { get; init; }
    public DateOnly? DataValidade { get; init; }
    public bool Vencida { get; init; }
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
    public bool? AzimuteValido { get; init; }
    public bool? ElevacaoValida { get; init; }
}
