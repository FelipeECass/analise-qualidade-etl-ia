using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;

namespace AnatelEtl;

internal static class Program
{
    private const string OutputName = "chatgpt_gpt6_zeroshot_r1.db";
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] States =
        "AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO".Split(' ');
    private static readonly string[] StationColumns =
        "fistel num_estacao uf cod_municipio municipio endereco latitude longitude coordenada_valida entidade operadora".Split(' ');
    private static readonly string[] EmissionColumns = (
        "uf fistel num_estacao num_ato tecnologia geracao designacao_emissao largura_banda_khz " +
        "freq_tx_mhz freq_rx_mhz duplex_mhz azimute potencia_w ganho_antena altura_antena_m " +
        "data_licenciamento data_primeiro_licenciamento data_validade vencida anos_licenciada " +
        "status classe_estacao polarizacao angulo_elevacao_graus angulo_meia_potencia_graus " +
        "frente_costa_db cod_tipo_antena cod_equipamento_antena cod_equipamento_transmissor " +
        "potencia_dbm azimute_valido elevacao_valida").Split(' ');
    private static readonly string[] KeyColumns =
        "uf fistel num_estacao num_ato azimute freq_tx_mhz freq_rx_mhz designacao_emissao tecnologia".Split(' ');
    private static readonly string[] RequiredHeaders = (
        "NumFistel NumEstacao NumAto SiglaUf CodMunicipio Municipio.NomeMunicipio NomeEntidade EnderecoEstacao " +
        "Latitude Longitude Tecnologia DesignacaoEmissao FreqTxMHz FreqRxMHz Azimute PotenciaTransmissorWatts " +
        "GanhoAntena AlturaAntena AnguloElevacao AnguloMeiaPotenciaAntena FrenteCostaAntena Polarizacao " +
        "CodTipoClasseEstacao CodTipoAntena CodEquipamentoAntena CodEquipamentoTransmissor Status.state " +
        "DataLicenciamento DataPrimeiroLicenciamento DataValidade").Split(' ');

    public static int Main()
    {
        try { Run(FindRoot()); return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Falha no ETL: {ex.Message}");
            return 1;
        }
    }

    private static string FindRoot()
    {
        const string windowsRoot = @"D:\Projects\TCC";
        if (OperatingSystem.IsWindows() && Directory.Exists(windowsRoot)) return windowsRoot;
        // Locate the Linux workspace mount independently of the current working directory.
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "src", "DDL.sql")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "files"))) return directory.FullName;
        throw new DirectoryNotFoundException("Raiz não encontrada (files e src/DDL.sql).");
    }

    private static void Run(string root)
    {
        var timer = Stopwatch.StartNew();
        var archives = States.Select(uf => Path.Combine(root, "files", uf + ".zip")).ToArray();
        foreach (var archive in archives)
            if (!File.Exists(archive)) throw new FileNotFoundException("ZIP obrigatório não encontrado.", archive);
        string ddl = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"), Encoding.UTF8);
        string outputDirectory = Path.Combine(root, "db");
        Directory.CreateDirectory(outputDirectory);
        Environment.SetEnvironmentVariable("SQLITE_TMPDIR", outputDirectory);
        string output = Path.Combine(outputDirectory, OutputName);
        // Reconstruct from an empty database, including on repeated invocations.
        foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(output + suffix);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = output, Pooling = false, ForeignKeys = true
        }.ToString());
        connection.Open();
        Execute(connection, "PRAGMA journal_mode = DELETE; PRAGMA synchronous = NORMAL; PRAGMA cache_size = -65536; PRAGMA temp_store = FILE;");
        Execute(connection, ddl);
        // Empty text is an unambiguous NULL sentinel after cleaning. SQLite compares REALs
        // numerically (55 = 55.0, and -0 = 0). TEMP state never changes the output schema.
        Execute(connection, """
            CREATE TEMP TABLE seen_emissao (
                uf TEXT NOT NULL, fistel TEXT NOT NULL, num_estacao TEXT NOT NULL,
                num_ato TEXT, azimute REAL, freq_tx_mhz REAL, freq_rx_mhz REAL,
                designacao_emissao TEXT, tecnologia TEXT
            );
            CREATE UNIQUE INDEX temp.ix_seen_emissao ON seen_emissao (
                uf, fistel, num_estacao, coalesce(num_ato, ''), coalesce(azimute, ''),
                coalesce(freq_tx_mhz, ''), coalesce(freq_rx_mhz, ''),
                coalesce(designacao_emissao, ''), coalesce(tecnologia, '')
            );
            """);
        using var station = InsertCommand(connection, "estacao", StationColumns, true);
        using var emission = InsertCommand(connection, "emissao", EmissionColumns);
        using var seen = InsertCommand(connection, "temp.seen_emissao", KeyColumns, true);
        long rows = 0, rejected = 0, duplicates = 0, stations = 0, emissions = 0;
        foreach (string archive in archives)
        {
            using var zip = ZipFile.OpenRead(archive);
            var entries = zip.Entries.Where(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length != 1) throw new InvalidDataException($"{Path.GetFileName(archive)} deve conter exatamente um CSV.");
            using var stream = entries[0].Open();
            using var parser = new TextFieldParser(stream, new UTF8Encoding(false, true), true)
            {
                TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
            };
            parser.SetDelimiters("|");
            string[] header = parser.ReadFields() ?? throw new InvalidDataException("CSV sem cabeçalho.");
            var columns = header.Select((name, index) => (name: name.Trim().TrimStart('\uFEFF'), index))
                .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);
            foreach (string name in RequiredHeaders)
                if (!columns.ContainsKey(name)) throw new InvalidDataException($"Coluna ausente em {Path.GetFileName(archive)}: {name}.");
            using var transaction = connection.BeginTransaction();
            station.Transaction = emission.Transaction = seen.Transaction = transaction;
            while (!parser.EndOfData)
            {
                string[]? fields = parser.ReadFields();
                if (fields == null) break;
                rows++;
                if (fields.Length != header.Length)
                    throw new InvalidDataException($"Quantidade de campos inválida em {Path.GetFileName(archive)}, registro {rows}.");
                string? Get(string name) => Clean(fields[columns[name]]);
                string? uf = Get("SiglaUf")?.ToUpperInvariant();
                string? fistel = Get("NumFistel"), number = Get("NumEstacao");
                if (uf == null || fistel == null || number == null) { rejected++; continue; }
                string? act = Get("NumAto"), technology = Get("Tecnologia")?.ToUpperInvariant();
                string? designation = Get("DesignacaoEmissao")?.ToUpperInvariant();
                double? azimuth = Number(Get("Azimute")), tx = Number(Get("FreqTxMHz")), rx = Number(Get("FreqRxMHz"));
                Bind(seen, uf, fistel, number, act, azimuth, tx, rx, designation, technology);
                if (seen.ExecuteNonQuery() == 0) { duplicates++; continue; }
                double? latitude = Number(Get("Latitude")), longitude = Number(Get("Longitude"));
                string? entity = Normalize(Get("NomeEntidade"));
                Bind(station, fistel, number, uf, Get("CodMunicipio"), Get("Municipio.NomeMunicipio"),
                    Normalize(Get("EnderecoEstacao")), latitude, longitude,
                    latitude is >= -33.75 and <= 5.28 && longitude is >= -73.99 and <= -28.83 &&
                    !(latitude == 0 && longitude == 0), entity, Operator(entity));
                stations += station.ExecuteNonQuery();
                double? power = Number(Get("PotenciaTransmissorWatts")), elevation = Number(Get("AnguloElevacao"));
                DateOnly? license = Date(Get("DataLicenciamento")), firstLicense = Date(Get("DataPrimeiroLicenciamento")), validity = Date(Get("DataValidade"));
                Bind(emission, uf, fistel, number, act, technology, Generation(technology), designation,
                    Bandwidth(designation), tx, rx, Duplex(tx, rx),
                    azimuth, power, Number(Get("GanhoAntena")), Number(Get("AlturaAntena")),
                    Format(license), Format(firstLicense), Format(validity), validity.HasValue && validity.Value < Today,
                    firstLicense.HasValue ? Round((Today.DayNumber - firstLicense.Value.DayNumber) / 365.25, 1) : null,
                    Get("Status.state"), Get("CodTipoClasseEstacao"), Get("Polarizacao")?.ToUpperInvariant(),
                    elevation, Number(Get("AnguloMeiaPotenciaAntena")), Number(Get("FrenteCostaAntena")),
                    Get("CodTipoAntena"), Get("CodEquipamentoAntena"), Get("CodEquipamentoTransmissor"),
                    power is > 0 ? Round(10 * (Math.Log10(power.Value) + 3), 1) : null,
                    ValidRange(azimuth, 0, 360), ValidRange(elevation, -90, 90));
                emission.ExecuteNonQuery();
                emissions++;
            }
            transaction.Commit();
            Console.WriteLine($"{Path.GetFileName(archive)}: {rows:N0} linhas lidas, {emissions:N0} emissões carregadas (acumulado).");
        }
        Execute(connection, "DROP TABLE temp.seen_emissao;");
        Console.WriteLine($"Concluído em {timer.Elapsed}: {stations} estações, {emissions} emissões, {rejected} rejeições, {duplicates} duplicatas. Banco: {output}");
    }

    private static SqliteCommand InsertCommand(SqliteConnection connection, string table, string[] columns, bool ignore = false)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"INSERT {(ignore ? "OR IGNORE " : "")}INTO {table} ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select(c => "$" + c))});";
        foreach (string column in columns) command.Parameters.Add(new SqliteParameter("$" + column, DBNull.Value));
        command.Prepare();
        return command;
    }

    private static void Bind(SqliteCommand command, params object?[] values)
    {
        if (values.Length != command.Parameters.Count) throw new InvalidOperationException("Número de parâmetros inconsistente.");
        for (int i = 0; i < values.Length; i++) command.Parameters[i].Value = values[i] ?? DBNull.Value;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static double? Number(string? value) => value != null &&
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float, Invariant, out double number) && double.IsFinite(number) ? number : null;
    private static DateOnly? Date(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", Invariant,
        DateTimeStyles.None, out var date) ? date : null;
    private static string? Format(DateOnly? date) => date?.ToString("yyyy-MM-dd", Invariant);
    private static double? Round(double value, int digits) => double.IsFinite(value) ? Math.Round(value, digits, MidpointRounding.AwayFromZero) : null;
    private static bool? ValidRange(double? value, double min, double max) => value.HasValue ? value.Value >= min && value.Value <= max : null;

    private static double? Duplex(double? tx, double? rx)
    {
        if (!tx.HasValue || !rx.HasValue) return null;
        // Subtract in decimal where possible so binary cancellation does not move a
        // decimal midpoint (e.g. 55.0015 - 55) to the wrong side before rounding.
        if (decimal.TryParse(tx.Value.ToString("R", Invariant), NumberStyles.Float, Invariant, out var left) &&
            decimal.TryParse(rx.Value.ToString("R", Invariant), NumberStyles.Float, Invariant, out var right))
        {
            try { return (double)decimal.Round(left - right, 3, MidpointRounding.AwayFromZero); }
            catch (OverflowException) { /* Finite doubles can exceed the decimal range. */ }
        }
        return Round(tx.Value - rx.Value, 3);
    }

    private static string? Normalize(string? value)
    {
        if (value == null) return null;
        var text = new StringBuilder();
        bool pendingSpace = false;
        foreach (var rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingSpace && text.Length > 0) text.Append(' ');
                text.Append(Rune.ToUpperInvariant(rune).ToString());
                pendingSpace = false;
            }
            else pendingSpace = true;
        }
        return text.Length == 0 ? null : text.ToString();
    }

    private static string Operator(string? entity)
    {
        if (entity == null) return "OUTRAS";
        string padded = " " + entity + " ";
        bool Word(string word) => padded.Contains(" " + word + " ", StringComparison.Ordinal);
        bool Starts(string word) => entity == word || entity.StartsWith(word + " ", StringComparison.Ordinal);
        if (Word("TELEFONICA") || Word("VIVO")) return "VIVO";
        if (Word("CLARO") || Word("EMBRATEL")) return "CLARO";
        if (Starts("TIM")) return "TIM";
        if (Starts("OI") || Word("TELEMAR") || Word("BRASIL TELECOM")) return "OI";
        if (Word("ALGAR")) return "ALGAR";
        if (Word("SERCOMTEL")) return "SERCOMTEL";
        if (Word("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    private static string Generation(string? technology) => technology switch
    {
        "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", null => "N/D", _ => "OUTRA"
    };

    private static double? Bandwidth(string? designation)
    {
        if (designation == null || designation.Length < 4) return null;
        int unitIndex = -1;
        double factor = 0;
        for (int i = 0; i < 4; i++)
        {
            char c = designation[i];
            if (c is >= '0' and <= '9') continue;
            if (i == 0 || unitIndex >= 0) return null;
            factor = c switch { 'H' => 0.001, 'K' => 1, 'M' => 1000, 'G' => 1_000_000, _ => 0 };
            if (factor == 0) return null;
            unitIndex = i;
        }
        if (unitIndex < 0) return null;
        string numeric = designation[..unitIndex] + "." + designation[(unitIndex + 1)..4];
        return double.Parse(numeric, Invariant) * factor;
    }
}
