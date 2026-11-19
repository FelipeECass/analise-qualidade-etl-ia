using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.Sqlite;

namespace AnatelEtl;

internal static class Program
{
    private const string Name = "chatgpt_gpt_6_zeroshot_r2";
    private static readonly string[] States =
        "AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO".Split(' ');

    public static int Main()
    {
        string? staging = null;
        string? dedup = null;
        try
        {
            var root = FindRoot();
            var ddl = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"), Encoding.UTF8);
            var inputs = States.Select(uf => Path.Combine(root, "files", uf + ".zip")).ToArray();
            foreach (var input in inputs)
                if (!File.Exists(input)) throw new FileNotFoundException("ZIP obrigatório não encontrado.", input);

            var directory = Path.Combine(root, "db");
            Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, Name + ".db");
            staging = output + ".building";
            dedup = output + ".dedup";
            File.Delete(staging);
            File.Delete(dedup);
            var clock = Stopwatch.StartNew();
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = staging,
                Pooling = false,
                ForeignKeys = true
            }.ToString()))
            {
                connection.Open();
                Execute(connection, "PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-65536;");
                Execute(connection, ddl); // Execute the supplied schema verbatim.
                using (var attach = connection.CreateCommand())
                {
                    attach.CommandText = "ATTACH DATABASE $path AS dedup;";
                    attach.Parameters.AddWithValue("$path", dedup);
                    attach.ExecuteNonQuery();
                }
                Execute(connection, "PRAGMA dedup.journal_mode=DELETE; PRAGMA dedup.synchronous=NORMAL; " +
                    "PRAGMA dedup.cache_size=-32768; CREATE TABLE dedup.seen (key TEXT PRIMARY KEY) WITHOUT ROWID;");
                var loader = new Loader(connection);
                foreach (var input in inputs)
                {
                    loader.Load(input);
                    Console.WriteLine($"{Path.GetFileName(input)}: lidas={loader.Read:N0}, rejeitadas={loader.Rejected:N0}, " +
                        $"duplicadas={loader.Duplicates:N0}, estações={loader.Stations:N0}, emissões={loader.Emissions:N0}; {clock.Elapsed:hh\\:mm\\:ss}");
                }
                Execute(connection, "DETACH DATABASE dedup;");
            }
            File.Delete(dedup);
            File.Move(staging, output, overwrite: true);
            Console.WriteLine($"Banco criado: {output} ({clock.Elapsed:hh\\:mm\\:ss})");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Falha no ETL: {exception.Message}");
            return 1;
        }
        finally
        {
            foreach (var path in new[] { staging, dedup })
                if (path is not null)
                {
                    try { File.Delete(path); File.Delete(path + "-journal"); }
                    catch (IOException) { /* Preserve the original error. */ }
                    catch (UnauthorizedAccessException) { }
                }
        }
    }

    private static string FindRoot()
    {
        // Locate the root relative to the project, on Windows and Linux.
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "src", "DDL.sql")) &&
                Directory.Exists(Path.Combine(folder.FullName, "files"))) return folder.FullName;
        throw new DirectoryNotFoundException("Não foi encontrada a raiz do projeto com src/DDL.sql e files.");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

internal sealed class Loader(SqliteConnection connection)
{
    private readonly HashSet<(string Uf, string Fistel, string Station)> stations = [];
    public long Read { get; private set; }
    public long Rejected { get; private set; }
    public long Duplicates { get; private set; }
    public long Emissions { get; private set; }
    public int Stations => stations.Count;

    private static readonly string[] RequiredHeaders =
    [
        "NumFistel", "NumEstacao", "NumAto", "SiglaUf", "CodMunicipio", "Municipio.NomeMunicipio",
        "NomeEntidade", "EnderecoEstacao", "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao",
        "FreqTxMHz", "FreqRxMHz", "Azimute", "PotenciaTransmissorWatts", "GanhoAntena", "AlturaAntena",
        "AnguloElevacao", "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao",
        "CodTipoClasseEstacao", "CodTipoAntena", "CodEquipamentoAntena", "CodEquipamentoTransmissor",
        "Status.state", "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade"
    ];

    public void Load(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries.Where(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length != 1) throw new InvalidDataException($"{path}: esperado um único CSV.");
        using var stream = entries[0].Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 65536);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = "|",
            HasHeaderRecord = true,
            IgnoreBlankLines = false,
            DetectColumnCountChanges = true,
            BufferSize = 65536
        });
        if (!csv.Read()) throw new InvalidDataException($"{path}: CSV vazio.");
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? throw new InvalidDataException("Cabeçalho ausente.");
        var positions = headers.Select((h, i) => (Name: h.Trim(), Index: i))
            .ToDictionary(h => h.Name, h => h.Index, StringComparer.Ordinal);
        foreach (var header in RequiredHeaders)
            if (!positions.ContainsKey(header)) throw new InvalidDataException($"{path}: coluna ausente: {header}.");
        string? Field(string name) => Transform.Clean(csv.GetField(positions[name]));

        using var transaction = connection.BeginTransaction();
        using var seen = connection.CreateCommand();
        seen.Transaction = transaction;
        seen.CommandText = "INSERT OR IGNORE INTO dedup.seen(key) VALUES ($key);";
        seen.Parameters.Add("$key", SqliteType.Text);
        seen.Prepare();
        using var station = InsertCommand("estacao", ["fistel", "num_estacao", "uf", "cod_municipio", "municipio",
            "endereco", "latitude", "longitude", "coordenada_valida", "entidade", "operadora"], transaction);
        using var emission = InsertCommand("emissao", ["uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao",
            "designacao_emissao", "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute",
            "potencia_w", "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
            "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
            "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
            "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido", "elevacao_valida"], transaction);

        while (csv.Read())
        {
            Read++;
            var fistel = Field("NumFistel");
            var number = Field("NumEstacao");
            var uf = Field("SiglaUf")?.ToUpperInvariant();
            if (fistel is null || number is null || uf is null) { Rejected++; continue; }
            var act = Field("NumAto");
            var azimuth = Transform.Number(Field("Azimute"));
            var tx = Transform.Number(Field("FreqTxMHz"));
            var rx = Transform.Number(Field("FreqRxMHz"));
            var designation = Field("DesignacaoEmissao")?.ToUpperInvariant();
            var technology = Field("Tecnologia")?.ToUpperInvariant();
            seen.Parameters[0].Value = Transform.Key(uf, fistel, number, act, azimuth, tx, rx, designation, technology);
            if (seen.ExecuteNonQuery() == 0) { Duplicates++; continue; }

            if (stations.Add((uf, fistel, number)))
            {
                var latitude = Transform.Number(Field("Latitude"));
                var longitude = Transform.Number(Field("Longitude"));
                var entity = Transform.Normalize(Field("NomeEntidade"));
                Write(station, fistel, number, uf, Field("CodMunicipio"), Field("Municipio.NomeMunicipio"),
                    Transform.Normalize(Field("EnderecoEstacao")), latitude, longitude,
                    latitude is >= -33.75 and <= 5.28 && longitude is >= -73.99 and <= -28.83 &&
                    !(latitude == 0 && longitude == 0), entity, Transform.Operator(entity));
            }

            var power = Transform.Number(Field("PotenciaTransmissorWatts"));
            var elevation = Transform.Number(Field("AnguloElevacao"));
            var licensed = Transform.Date(Field("DataLicenciamento"));
            var first = Transform.Date(Field("DataPrimeiroLicenciamento"));
            var validity = Transform.Date(Field("DataValidade"));
            Write(emission, uf, fistel, number, act, technology, Transform.Generation(technology), designation,
                Transform.Bandwidth(designation), tx, rx, Transform.Duplex(tx, rx), azimuth, power,
                Transform.Number(Field("GanhoAntena")), Transform.Number(Field("AlturaAntena")),
                Transform.DateText(licensed), Transform.DateText(first), Transform.DateText(validity),
                validity.HasValue && validity.Value < Transform.Today,
                first.HasValue ? Transform.Round((Transform.Today.DayNumber - first.Value.DayNumber) / 365.25, 1) : null,
                Field("Status.state"), Field("CodTipoClasseEstacao"), Field("Polarizacao")?.ToUpperInvariant(),
                elevation, Transform.Number(Field("AnguloMeiaPotenciaAntena")), Transform.Number(Field("FrenteCostaAntena")),
                Field("CodTipoAntena"), Field("CodEquipamentoAntena"), Field("CodEquipamentoTransmissor"),
                power is > 0 ? Transform.Round(10 * (Math.Log10(power.Value) + 3), 1) : null,
                Transform.InRange(azimuth, 0, 360), Transform.InRange(elevation, -90, 90));
            Emissions++;
        }
        transaction.Commit();
    }

    private SqliteCommand InsertCommand(string table, string[] columns, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {table} ({string.Join(',', columns)}) VALUES " +
            $"({string.Join(',', columns.Select((_, i) => "$p" + i))});";
        for (var i = 0; i < columns.Length; i++) command.Parameters.AddWithValue("$p" + i, DBNull.Value);
        command.Prepare();
        return command;
    }

    private static void Write(SqliteCommand command, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
            command.Parameters[i].Value = values[i] switch { null => DBNull.Value, bool b => b ? 1 : 0, var v => v };
        command.ExecuteNonQuery();
    }
}
