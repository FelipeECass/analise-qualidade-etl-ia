namespace AnatelEtl;

internal enum Field
{
    Fistel, Station, Act, Uf, MunicipalityCode, Municipality, Entity, Address, Latitude, Longitude,
    Technology, Designation, Tx, Rx, Azimuth, Power, Gain, Height, Elevation, BeamAngle,
    FrontBack, Polarization, StationClass, AntennaType, AntennaEquipment, TransmitterEquipment,
    Status, Licensed, FirstLicensed, ValidUntil
}

internal sealed class SourceSchema
{
    private static readonly string[] Names =
    [
        "NumFistel", "NumEstacao", "NumAto", "SiglaUf", "CodMunicipio", "Municipio.NomeMunicipio",
        "NomeEntidade", "EnderecoEstacao", "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao",
        "FreqTxMHz", "FreqRxMHz", "Azimute", "PotenciaTransmissorWatts", "GanhoAntena", "AlturaAntena",
        "AnguloElevacao", "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao",
        "CodTipoClasseEstacao", "CodTipoAntena", "CodEquipamentoAntena", "CodEquipamentoTransmissor",
        "Status.state", "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade"
    ];
    private readonly int[] indices;
    public int Count { get; }

    public SourceSchema(string[] header)
    {
        Count = header.Length;
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Length; i++)
            if (!lookup.TryAdd(header[i].Trim(), i))
                throw new FormatException($"Coluna de cabeçalho duplicada: {header[i]}.");
        indices = Names.Select(name => lookup.TryGetValue(name, out int index) ? index
            : throw new FormatException($"Coluna de entrada ausente: {name}.")).ToArray();
    }

    public string? Get(string[] row, Field field)
    {
        int index = indices[(int)field];
        return index < row.Length ? row[index] : null;
    }
}
