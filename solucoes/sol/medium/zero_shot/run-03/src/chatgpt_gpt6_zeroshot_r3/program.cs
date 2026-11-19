using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AnatelEtl;

internal static class Program
{
    internal const string Name = "chatgpt_gpt6_zeroshot_r3";
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly string[] States = "AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO".Split(' ');

    public static int Main()
    {
        try { Run(FindRoot()); return 0; }
        catch (Exception ex) { Console.Error.WriteLine($"Falha no ETL: {ex.Message}"); return 1; }
    }

    private static string FindRoot()
    {
        // Layout relativo equivalente em Windows e Linux, sem depender do diretório atual.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "DDL.sql")) && Directory.Exists(Path.Combine(directory.FullName, "files")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Não foi localizada a raiz com files/ e src/DDL.sql.");
    }

    private static void Run(string root)
    {
        var watch = Stopwatch.StartNew();
        var inputs = States.Select(uf => Path.Combine(root, "files", uf + ".zip")).ToArray();
        foreach (var input in inputs)
            if (!File.Exists(input)) throw new FileNotFoundException("ZIP obrigatório não encontrado.", input);
        var outputDirectory = Path.Combine(root, "db");
        Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, Name + ".db");
        // Toda execução começa com um banco vazio, inclusive quando repetida manualmente.
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(output + suffix);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = output, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-65536; PRAGMA temp_store=FILE;");
        Execute(connection, File.ReadAllText(Path.Combine(root, "src", "DDL.sql"), Encoding.UTF8));
        // TEMP não altera o esquema persistente. Texto vazio é uma sentinela segura:
        // valores vazios viram NULL e números REAL nunca se confundem com texto.
        Execute(connection, """
            CREATE TEMP TABLE vistas (
                uf TEXT NOT NULL, fistel TEXT NOT NULL, num_estacao TEXT NOT NULL,
                num_ato TEXT, azimute REAL, freq_tx_mhz REAL, freq_rx_mhz REAL,
                designacao_emissao TEXT, tecnologia TEXT
            );
            CREATE UNIQUE INDEX temp.ix_vistas ON vistas (
                uf, fistel, num_estacao, coalesce(num_ato,''), coalesce(azimute,''),
                coalesce(freq_tx_mhz,''), coalesce(freq_rx_mhz,''),
                coalesce(designacao_emissao,''), coalesce(tecnologia,'')
            );
            """);
        using var seen = Insert(connection, "vistas", "uf fistel num_estacao num_ato azimute freq_tx_mhz freq_rx_mhz designacao_emissao tecnologia", true);
        using var station = Insert(connection, "estacao", "fistel num_estacao uf cod_municipio municipio endereco latitude longitude coordenada_valida entidade operadora");
        using var emission = Insert(connection, "emissao", "uf fistel num_estacao num_ato tecnologia geracao designacao_emissao largura_banda_khz freq_tx_mhz freq_rx_mhz duplex_mhz azimute potencia_w ganho_antena altura_antena_m data_licenciamento data_primeiro_licenciamento data_validade vencida anos_licenciada status classe_estacao polarizacao angulo_elevacao_graus angulo_meia_potencia_graus frente_costa_db cod_tipo_antena cod_equipamento_antena cod_equipamento_transmissor potencia_dbm azimute_valido elevacao_valida");
        var stations = new HashSet<(string Uf, string Fistel, string Station)>();
        long read = 0, rejected = 0, duplicated = 0, loaded = 0;
        foreach (var input in inputs)
        {
            using var archive = ZipFile.OpenRead(input);
            var entries = archive.Entries.Where(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length != 1) throw new InvalidDataException($"{input}: esperado exatamente um CSV.");
            using var stream = entries[0].Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 65536);
            using var records = Csv.Read(reader).GetEnumerator();
            if (!records.MoveNext()) throw new InvalidDataException($"CSV vazio em {input}.");
            var columns = new Columns(records.Current);
            using var transaction = connection.BeginTransaction();
            seen.Transaction = station.Transaction = emission.Transaction = transaction;
            while (records.MoveNext())
            {
                ++read;
                var row = columns.Bind(records.Current);
                var fistel = row.Text("NumFistel");
                var number = row.Text("NumEstacao");
                var uf = row.Upper("SiglaUf");
                if (fistel == null || number == null || uf == null) { ++rejected; continue; }
                var act = row.Text("NumAto");
                var azimuth = row.Number("Azimute");
                var tx = row.Number("FreqTxMHz");
                var rx = row.Number("FreqRxMHz");
                var designation = row.Upper("DesignacaoEmissao");
                var technology = row.Upper("Tecnologia");
                Set(seen, uf, fistel, number, act, azimuth, tx, rx, designation, technology);
                if (seen.ExecuteNonQuery() == 0) { ++duplicated; continue; }
                if (stations.Add((uf, fistel, number)))
                {
                    var entity = Transform.Normalize(row.Text("NomeEntidade"));
                    var latitude = row.Number("Latitude");
                    var longitude = row.Number("Longitude");
                    var validCoordinate = latitude is >= -33.75 and <= 5.28 && longitude is >= -73.99 and <= -28.83 && (latitude != 0 || longitude != 0);
                    Set(station, fistel, number, uf, row.Text("CodMunicipio"), row.Text("Municipio.NomeMunicipio"),
                        Transform.Normalize(row.Text("EnderecoEstacao")), latitude, longitude, validCoordinate ? 1 : 0, entity, Transform.Operator(entity));
                    station.ExecuteNonQuery();
                }
                var power = row.Number("PotenciaTransmissorWatts");
                var elevation = row.Number("AnguloElevacao");
                var licensed = row.Date("DataLicenciamento");
                var firstLicensed = row.Date("DataPrimeiroLicenciamento");
                var expiry = row.Date("DataValidade");
                Set(emission, uf, fistel, number, act, technology, Transform.Generation(technology), designation,
                    Transform.Bandwidth(designation), tx, rx,
                    tx.HasValue && rx.HasValue ? Transform.Round(tx.Value - rx.Value, 3) : null,
                    azimuth, power, row.Number("GanhoAntena"), row.Number("AlturaAntena"),
                    DateText(licensed), DateText(firstLicensed), DateText(expiry), expiry < Today ? 1 : 0,
                    firstLicensed.HasValue ? Transform.Round((Today.DayNumber - firstLicensed.Value.DayNumber) / 365.25, 1) : null,
                    row.Text("Status.state"), row.Text("CodTipoClasseEstacao"), row.Upper("Polarizacao"),
                    elevation, row.Number("AnguloMeiaPotenciaAntena"), row.Number("FrenteCostaAntena"),
                    row.Text("CodTipoAntena"), row.Text("CodEquipamentoAntena"), row.Text("CodEquipamentoTransmissor"),
                    power > 0 ? Transform.Round(10 * (Math.Log10(power.Value) + 3), 1) : null,
                    Transform.Range(azimuth, 0, 360), Transform.Range(elevation, -90, 90));
                emission.ExecuteNonQuery();
                ++loaded;
            }
            transaction.Commit();
            Console.WriteLine($"{Path.GetFileName(input)}: lidas={read:N0}; estações={stations.Count:N0}; emissões={loaded:N0}; rejeitadas={rejected:N0}; duplicadas={duplicated:N0}; tempo={watch.Elapsed:hh\\:mm\\:ss}");
        }
        Execute(connection, "DROP TABLE temp.vistas;");
        Console.WriteLine($"Concluído: {output}");
    }

    private static string? DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static SqliteCommand Insert(SqliteConnection connection, string table, string names, bool ignore = false)
    {
        var columns = names.Split(' ');
        var command = connection.CreateCommand();
        command.CommandText = $"INSERT {(ignore ? "OR IGNORE " : "")}INTO {table} ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select((_, i) => "$p" + i))})";
        for (var i = 0; i < columns.Length; ++i) command.Parameters.Add(new SqliteParameter("$p" + i, DBNull.Value));
        command.Prepare();
        return command;
    }
    private static void Set(SqliteCommand command, params object?[] values)
    {
        for (var i = 0; i < values.Length; ++i) command.Parameters[i].Value = values[i] ?? DBNull.Value;
    }
}

internal static class Transform
{
    public static double? Number(string? text) => double.TryParse(text?.Replace(',', '.'),
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
        CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? (value == 0 ? 0 : value) : null;
    public static double? Round(double value, int digits) => double.IsFinite(value) ? Math.Round(value, digits, MidpointRounding.AwayFromZero) : null;
    public static int? Range(double? value, double minimum, double maximum) => value.HasValue ? (value >= minimum && value <= maximum ? 1 : 0) : null;
    public static string? Normalize(string? text)
    {
        if (text == null) return null;
        var result = new StringBuilder();
        var space = false;
        foreach (var rune in text.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (Rune.IsLetterOrDigit(rune))
            {
                if (space && result.Length != 0) result.Append(' ');
                result.Append(Rune.ToUpperInvariant(rune).ToString());
                space = false;
            }
            else space = true;
        }
        return result.Length == 0 ? null : result.ToString();
    }
    private static readonly (Regex Pattern, string Value)[] Operators =
    [
        (new(@"\b(TELEFONICA|VIVO)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "VIVO"),
        (new(@"\b(CLARO|EMBRATEL)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "CLARO"),
        (new(@"^TIM\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "TIM"),
        (new(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "OI"),
        (new(@"\bALGAR\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "ALGAR"),
        (new(@"\bSERCOMTEL\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "SERCOMTEL"),
        (new(@"\bBRISANET\b", RegexOptions.Compiled | RegexOptions.CultureInvariant), "BRISANET")
    ];
    public static string Operator(string? entity) => entity == null ? "OUTRAS" : Operators.FirstOrDefault(item => item.Pattern.IsMatch(entity)).Value ?? "OUTRAS";
    public static string Generation(string? technology) => technology switch
    {
        "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", null => "N/D", _ => "OUTRA"
    };
    public static double? Bandwidth(string? designation)
    {
        if (designation == null || designation.Length < 4) return null;
        var unit = -1;
        double factor = 0;
        for (var i = 0; i < 4; ++i)
        {
            var c = designation[i];
            if (c is >= '0' and <= '9') continue;
            if (i == 0 || unit >= 0) return null;
            factor = c switch { 'H' => .001, 'K' => 1, 'M' => 1000, 'G' => 1_000_000, _ => 0 };
            if (factor == 0) return null;
            unit = i;
        }
        if (unit < 0) return null;
        return double.Parse(designation[..4].Replace(designation[unit], '.'), CultureInfo.InvariantCulture) * factor;
    }
}

internal sealed class Columns
{
    private static readonly string[] Required = ("NumFistel NumEstacao NumAto SiglaUf CodMunicipio Municipio.NomeMunicipio NomeEntidade EnderecoEstacao Latitude Longitude Tecnologia DesignacaoEmissao FreqTxMHz FreqRxMHz Azimute PotenciaTransmissorWatts GanhoAntena AlturaAntena AnguloElevacao AnguloMeiaPotenciaAntena FrenteCostaAntena Polarizacao CodTipoClasseEstacao CodTipoAntena CodEquipamentoAntena CodEquipamentoTransmissor Status.state DataLicenciamento DataPrimeiroLicenciamento DataValidade").Split(' ');
    private readonly Dictionary<string, int> indexes;
    private readonly int length;
    private string[] values = [];
    public Columns(string[] header)
    {
        length = header.Length;
        indexes = header.Select((name, index) => (name: name.Trim().TrimStart('\uFEFF'), index)).ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
        foreach (var name in Required)
            if (!indexes.ContainsKey(name)) throw new InvalidDataException($"Coluna obrigatória ausente: {name}");
    }
    public Columns Bind(string[] row)
    {
        if (row.Length != length) throw new InvalidDataException($"Registro com {row.Length} campos; esperados {length}.");
        values = row;
        return this;
    }
    public string? Text(string name)
    {
        var value = values[indexes[name]].Trim();
        return value.Length == 0 ? null : value;
    }
    public string? Upper(string name) => Text(name)?.ToUpperInvariant();
    public double? Number(string name) => Transform.Number(Text(name));
    public DateOnly? Date(string name) => DateOnly.TryParseExact(Text(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}

internal static class Csv
{
    // Suporta campos com |, aspas duplicadas, CR/LF internos e cabeçalho sem aspas.
    public static IEnumerable<string[]> Read(TextReader reader)
    {
        var buffer = new char[65536];
        var field = new StringBuilder();
        var fields = new List<string>();
        var quoted = false;
        var afterQuote = false;
        var skipLf = false;
        var started = false;
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
        {
            for (var i = 0; i < count; ++i)
            {
                var c = buffer[i];
                if (skipLf) { skipLf = false; if (c == '\n') continue; }
                started = true;
                if (quoted)
                {
                    if (c == '"') { quoted = false; afterQuote = true; }
                    else field.Append(c);
                    continue;
                }
                if (afterQuote && c == '"') { field.Append('"'); quoted = true; afterQuote = false; continue; }
                if (c == '|') { fields.Add(field.ToString()); field.Clear(); afterQuote = false; }
                else if (c is '\r' or '\n')
                {
                    fields.Add(field.ToString()); field.Clear();
                    yield return fields.ToArray();
                    fields.Clear(); afterQuote = false; started = false; skipLf = c == '\r';
                }
                else if (c == '"' && field.Length == 0 && !afterQuote) quoted = true;
                else if (afterQuote)
                {
                    if (!char.IsWhiteSpace(c)) throw new InvalidDataException("Caractere inesperado após fechamento de aspas no CSV.");
                }
                else field.Append(c);
            }
        }
        if (quoted) throw new InvalidDataException("CSV terminou dentro de um campo entre aspas.");
        if (started || fields.Count != 0 || field.Length != 0) { fields.Add(field.ToString()); yield return fields.ToArray(); }
    }
}
