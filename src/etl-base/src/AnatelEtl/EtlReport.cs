namespace AnatelEtl;

/// <summary>Contadores de qualidade coletados durante o ETL.</summary>
public sealed class EtlReport
{
    public const string RowsRead = "linhas_lidas";
    public const string RejectedPrefix = "rejeitadas:";
    public const string DuplicateEmissions = "emissoes_duplicadas";
    public const string InvalidCoordinates = "estacoes_coordenada_invalida";
    public const string ExpiredLicenses = "emissoes_vencidas";
    public const string InvalidAzimuth = "emissoes_azimute_invalido";
    public const string InvalidElevation = "emissoes_elevacao_invalida";
    public const string InconsistentDuplex = "emissoes_duplex_incoerente";
    public const string StationsLoaded = "estacoes_carregadas";
    public const string EmissionsLoaded = "emissoes_carregadas";

    private readonly SortedDictionary<string, int> counters = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Counters => counters;

    public void Increment(string key, int amount = 1) =>
        counters[key] = counters.GetValueOrDefault(key) + amount;

    public int Get(string key) => counters.GetValueOrDefault(key);
}
