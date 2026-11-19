using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace AnatelEtl;

internal readonly record struct StationKey(string Uf, string Fistel, string Number);
internal readonly record struct EmissionKey(StationKey Station, string? Act, double? Azimuth,
    double? Tx, double? Rx, string? Designation, string? Technology);

internal sealed class Pipeline
{
    internal static bool CreatedOutput { get; private set; }
    private readonly HashSet<StationKey> stations = [];
    private readonly HashSet<EmissionKey> emissions = [];
    private static readonly string[] StationColumns =
    ["fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude", "coordenada_valida", "entidade", "operadora"];
    private static readonly string[] EmissionColumns =
    [
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao", "largura_banda_khz",
        "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w", "ganho_antena", "altura_antena_m",
        "data_licenciamento", "data_primeiro_licenciamento", "data_validade", "vencida", "anos_licenciada",
        "status", "classe_estacao", "polarizacao", "angulo_elevacao_graus", "angulo_meia_potencia_graus",
        "frente_costa_db", "cod_tipo_antena", "cod_equipamento_antena", "cod_equipamento_transmissor",
        "potencia_dbm", "azimute_valido", "elevacao_valida"
    ];

    private static SqliteCommand Insert(SqliteConnection connection, string table, string[] columns)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select((_, i) => "$p" + i))})";
        for (int i = 0; i < columns.Length; i++) command.Parameters.Add(new SqliteParameter("$p" + i, DBNull.Value));
        command.Prepare();
        return command;
    }
    private static void Write(SqliteCommand command, params object?[] values)
    {
        if (values.Length != command.Parameters.Count) throw new InvalidOperationException("Mapeamento SQL inconsistente.");
        for (int i = 0; i < values.Length; i++) command.Parameters[i].Value = values[i] ?? DBNull.Value;
        command.ExecuteNonQuery();
    }
    internal void Run(string root, string output)
    {
        var watch = Stopwatch.StartNew();
        var ufs = "AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO".Split(' ');
        var inputs = ufs.Select(uf => Path.Combine(root, "files", uf + ".zip")).ToArray();
        foreach (var input in inputs)
            if (!File.Exists(input)) throw new FileNotFoundException("ZIP obrigatório ausente.", input);
        // CreateNew protects any existing output even if another process races with the preflight check.
        using (new FileStream(output, FileMode.CreateNew, FileAccess.Write)) { }
        CreatedOutput = true;
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = output, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-65536;";
            setup.ExecuteNonQuery();
            setup.CommandText = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"));
            setup.ExecuteNonQuery();
        }
        using var stationInsert = Insert(connection, "estacao", StationColumns);
        using var emissionInsert = Insert(connection, "emissao", EmissionColumns);
        SqliteTransaction? transaction = null;
        long rows = 0, rejected = 0, duplicates = 0, loaded = 0;
        int batch = 0;
        try
        {
            transaction = connection.BeginTransaction();
            stationInsert.Transaction = transaction;
            emissionInsert.Transaction = transaction;
            foreach (var input in inputs)
            {
                using var source = new CsvSource(input);
                var before = loaded;
                while (source.Read() is { } row)
                {
                    rows++;
                    string? S(string name) => source.Get(row, name);
                    double? N(string name) => Transform.Number(S(name));
                    var fistel = S("NumFistel");
                    var number = S("NumEstacao");
                    var uf = Transform.Upper(S("SiglaUf"));
                    if (fistel is null || number is null || uf is null) { rejected++; continue; }
                    var station = new StationKey(uf, fistel, number);
                    if (stations.Add(station))
                    {
                        var lat = N("Latitude");
                        var lon = N("Longitude");
                        var entity = Transform.Normalize(S("NomeEntidade"));
                        Write(stationInsert, fistel, number, uf, S("CodMunicipio"), S("Municipio.NomeMunicipio"),
                            Transform.Normalize(S("EnderecoEstacao")), lat, lon, Transform.Coordinates(lat, lon), entity, Transform.Operator(entity));
                    }
                    var act = S("NumAto");
                    var azimuth = N("Azimute");
                    var tx = N("FreqTxMHz");
                    var rx = N("FreqRxMHz");
                    var designation = Transform.Upper(S("DesignacaoEmissao"));
                    var technology = Transform.Upper(S("Tecnologia"));
                    if (!emissions.Add(new(station, act, azimuth, tx, rx, designation, technology))) { duplicates++; continue; }
                    var watts = N("PotenciaTransmissorWatts");
                    var elevation = N("AnguloElevacao");
                    var license = Transform.Date(S("DataLicenciamento"));
                    var first = Transform.Date(S("DataPrimeiroLicenciamento"));
                    var validity = Transform.Date(S("DataValidade"));
                    Write(emissionInsert, uf, fistel, number, act, technology, Transform.Generation(technology), designation,
                        Transform.Bandwidth(designation), tx, rx, Transform.Duplex(tx, rx), azimuth, watts, N("GanhoAntena"), N("AlturaAntena"),
                        Transform.DateText(license), Transform.DateText(first), Transform.DateText(validity), Transform.Expired(validity), Transform.Years(first),
                        S("Status.state"), S("CodTipoClasseEstacao"), Transform.Upper(S("Polarizacao")), elevation,
                        N("AnguloMeiaPotenciaAntena"), N("FrenteCostaAntena"), S("CodTipoAntena"), S("CodEquipamentoAntena"),
                        S("CodEquipamentoTransmissor"), Transform.PowerDbm(watts), Transform.InRange(azimuth, 0, 360), Transform.InRange(elevation, -90, 90));
                    loaded++;
                    if (++batch == 25000)
                    {
                        transaction.Commit();
                        transaction.Dispose();
                        transaction = connection.BeginTransaction();
                        stationInsert.Transaction = transaction;
                        emissionInsert.Transaction = transaction;
                        batch = 0;
                    }
                }
                Console.WriteLine($"{Path.GetFileName(input)}: +{loaded - before:N0} emissões; total {loaded:N0}; {watch.Elapsed:hh\\:mm\\:ss}");
            }
            transaction.Commit();
        }
        finally { transaction?.Dispose(); }
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check";
            if (!Equals(check.ExecuteScalar(), "ok")) throw new InvalidDataException("SQLite quick_check falhou.");
            check.CommandText = "PRAGMA foreign_key_check";
            using var reader = check.ExecuteReader();
            if (reader.Read()) throw new InvalidDataException("SQLite foreign_key_check falhou.");
        }
        Console.WriteLine($"Concluído: lidas={rows}; rejeitadas={rejected}; duplicadas={duplicates}; estações={stations.Count}; emissões={loaded}; tempo={watch.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine($"Banco: {output}");
    }
}
