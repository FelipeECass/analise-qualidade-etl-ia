namespace Etl;

public sealed class EstacaoRow
{
    public required string Fistel;
    public required string NumEstacao;
    public required string Uf;
    public string? CodMunicipio;
    public string? Municipio;
    public string? Endereco;
    public double? Latitude;
    public double? Longitude;
    public bool CoordenadaValida;
    public string? Entidade;
    public required string Operadora;
}

public sealed class EmissaoRow
{
    public required string Uf;
    public required string Fistel;
    public required string NumEstacao;
    public string? NumAto;
    public string? Tecnologia;
    public required string Geracao;
    public string? DesignacaoEmissao;
    public double? LarguraBandaKhz;
    public double? FreqTxMhz;
    public double? FreqRxMhz;
    public double? DuplexMhz;
    public double? Azimute;
    public double? PotenciaW;
    public double? GanhoAntena;
    public double? AlturaAntenaM;
    public DateOnly? DataLicenciamento;
    public DateOnly? DataPrimeiroLicenciamento;
    public DateOnly? DataValidade;
    public bool Vencida;
    public double? AnosLicenciada;
    public string? Status;
    public string? ClasseEstacao;
    public string? Polarizacao;
    public double? AnguloElevacaoGraus;
    public double? AnguloMeiaPotenciaGraus;
    public double? FrenteCostaDb;
    public string? CodTipoAntena;
    public string? CodEquipamentoAntena;
    public string? CodEquipamentoTransmissor;
    public double? PotenciaDbm;
    public bool? AzimuteValido;
    public bool? ElevacaoValida;
}
