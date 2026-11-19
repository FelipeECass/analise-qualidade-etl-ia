namespace AnatelEtl;

public sealed record Estacao(
    string Fistel, string NumEstacao, string Uf, string? CodMunicipio, string? Municipio, string? Endereco,
    double? Latitude, double? Longitude, bool CoordenadaValida, string? Entidade, string Operadora);

public sealed record Emissao(
    string Uf, string Fistel, string NumEstacao, string? NumAto, string? Tecnologia, string Geracao,
    string? DesignacaoEmissao, double? LarguraBandaKhz, double? FreqTxMhz, double? FreqRxMhz, double? DuplexMhz,
    double? Azimute, double? PotenciaW, double? GanhoAntena, double? AlturaAntenaM,
    DateOnly? DataLicenciamento, DateOnly? DataPrimeiroLicenciamento, DateOnly? DataValidade,
    bool Vencida, double? AnosLicenciada, string? Status, string? ClasseEstacao, string? Polarizacao,
    double? AnguloElevacaoGraus, double? AnguloMeiaPotenciaGraus, double? FrenteCostaDb,
    string? CodTipoAntena, string? CodEquipamentoAntena, string? CodEquipamentoTransmissor,
    double? PotenciaDbm, bool? AzimuteValido, bool? ElevacaoValida);
