using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;

try { Pipeline.Run(); return 0; }
catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }

static class Pipeline
{
    static readonly DateOnly Today = new(2026, 10, 15);
    static readonly string[] Ufs = "AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO".Split(' ');
    static readonly string[] Required = "NumFistel NumEstacao NumAto SiglaUf CodMunicipio Municipio.NomeMunicipio NomeEntidade EnderecoEstacao Latitude Longitude Tecnologia DesignacaoEmissao FreqTxMHz FreqRxMHz Azimute PotenciaTransmissorWatts GanhoAntena AlturaAntena AnguloElevacao AnguloMeiaPotenciaAntena FrenteCostaAntena Polarizacao CodTipoClasseEstacao CodTipoAntena CodEquipamentoAntena CodEquipamentoTransmissor Status.state DataLicenciamento DataPrimeiroLicenciamento DataValidade".Split(' ');
    const string StationColumns = "fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora";
    const string EmissionColumns = "uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida";

    public static void Run()
    {
        string root = OperatingSystem.IsWindows() ? @"D:\Projects\TCC" : FindRoot();
        string db = Path.Combine(root, "db", "chatgpt_gpt6_completo_r2.db");
        Directory.CreateDirectory(Path.GetDirectoryName(db)!);
        if (File.Exists(db)) File.Delete(db);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db }.ToString());
        connection.Open();
        Exec(connection, "PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA temp_store=FILE;");
        Exec(connection, File.ReadAllText(Path.Combine(root, "src", "DDL.sql")));
        Exec(connection, "CREATE TEMP TABLE seen(uf TEXT, fistel TEXT, est TEXT, ato TEXT, az REAL, tx REAL, rx REAL, des TEXT, tec TEXT); CREATE UNIQUE INDEX seen_key ON seen(uf,fistel,est,coalesce(ato,''),coalesce(az,''),coalesce(tx,''),coalesce(rx,''),coalesce(des,''),coalesce(tec,''));");
        using var transaction = connection.BeginTransaction();
        using var station = Command(connection, $"INSERT OR IGNORE INTO estacao ({StationColumns}) VALUES ({Placeholders(11)})", 11);
        using var emission = Command(connection, $"INSERT INTO emissao ({EmissionColumns}) VALUES ({Placeholders(32)})", 32);
        using var seen = Command(connection, $"INSERT OR IGNORE INTO seen VALUES ({Placeholders(9)})", 9);
        long rows = 0, rejected = 0, duplicates = 0, inserted = 0;
        foreach (var ufFile in Ufs)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(root, "files", ufFile + ".zip"));
            var files = zip.Entries.Where(x => !string.IsNullOrEmpty(x.Name)).ToArray();
            if (files.Length != 1 || !files[0].Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"ZIP {ufFile}: esperado um CSV.");
            using var stream = files[0].Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            using var csv = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            csv.SetDelimiters("|");
            var header = csv.ReadFields() ?? throw new InvalidDataException($"CSV {ufFile} vazio.");
            var index = header.Select((name, i) => (name: name.Trim().TrimStart('\uFEFF'), i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
            foreach (var name in Required) if (!index.ContainsKey(name)) throw new InvalidDataException($"CSV {ufFile}: coluna ausente: {name}");
            while (!csv.EndOfData)
            {
                var fields = csv.ReadFields(); if (fields is null) continue;
                rows++;
                if (fields.Length != header.Length) throw new InvalidDataException($"CSV {ufFile}, linha {csv.LineNumber}: número de campos incorreto.");
                string? S(string name) { var value = fields[index[name]].Trim(); return value.Length == 0 ? null : value; }
                double? N(string name) => Number(S(name));
                var fistel = S("NumFistel"); var est = S("NumEstacao"); var uf = S("SiglaUf")?.ToUpperInvariant();
                if (fistel is null || est is null || uf is null) { rejected++; continue; }
                var ato = S("NumAto"); var tech = S("Tecnologia")?.ToUpperInvariant(); var des = S("DesignacaoEmissao")?.ToUpperInvariant();
                var az = N("Azimute"); var tx = N("FreqTxMHz"); var rx = N("FreqRxMHz");
                var latitude = N("Latitude"); var longitude = N("Longitude"); var entity = Normalize(S("NomeEntidade"));
                Bind(station, fistel, est, uf, S("CodMunicipio"), S("Municipio.NomeMunicipio"), Normalize(S("EnderecoEstacao")), latitude, longitude,
                    latitude is >= -33.75 and <= 5.28 && longitude is >= -73.99 and <= -28.83 && (latitude != 0 || longitude != 0) ? 1 : 0, entity, Operator(entity));
                station.ExecuteNonQuery();
                Bind(seen, uf, fistel, est, ato, az, tx, rx, des, tech);
                if (seen.ExecuteNonQuery() == 0) { duplicates++; continue; }
                var power = N("PotenciaTransmissorWatts"); var elevation = N("AnguloElevacao");
                var first = Date(S("DataPrimeiroLicenciamento")); var validity = Date(S("DataValidade"));
                Bind(emission, uf, fistel, est, ato, tech, Generation(tech), des, Bandwidth(des), tx, rx,
                    tx.HasValue && rx.HasValue ? FiniteRound(tx.Value - rx.Value, 3) : null, az, power, N("GanhoAntena"), N("AlturaAntena"),
                    Date(S("DataLicenciamento"))?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), first?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), validity?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    validity.HasValue && validity.Value < Today ? 1 : 0, first.HasValue ? Math.Round((Today.DayNumber - first.Value.DayNumber) / 365.25, 1, MidpointRounding.AwayFromZero) : null,
                    S("Status.state"), S("CodTipoClasseEstacao"), S("Polarizacao")?.ToUpperInvariant(), elevation, N("AnguloMeiaPotenciaAntena"), N("FrenteCostaAntena"), S("CodTipoAntena"), S("CodEquipamentoAntena"), S("CodEquipamentoTransmissor"),
                    power is > 0 ? FiniteRound(10 * (Math.Log10(power.Value) + 3), 1) : null, Valid(az, 0, 360), Valid(elevation, -90, 90));
                emission.ExecuteNonQuery(); inserted++;
            }
            Console.WriteLine($"{ufFile}: linhas={rows}, emissões={inserted}, duplicadas={duplicates}, rejeitadas={rejected}");
        }
        transaction.Commit();
    }
    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null) { if (File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")) && Directory.Exists(Path.Combine(dir.FullName, "files"))) return dir.FullName; dir = dir.Parent; }
        throw new DirectoryNotFoundException("Raiz do projeto não encontrada.");
    }
    static void Exec(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    static string Placeholders(int n) => string.Join(',', Enumerable.Range(0, n).Select(i => "$p" + i));
    static SqliteCommand Command(SqliteConnection c, string sql, int count) { var cmd = c.CreateCommand(); cmd.CommandText = sql; for (int i = 0; i < count; i++) cmd.Parameters.AddWithValue("$p" + i, DBNull.Value); cmd.Prepare(); return cmd; }
    static void Bind(SqliteCommand cmd, params object?[] values) { for (int i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value; }
    static double? Number(string? s) => s != null && double.TryParse(s.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    static DateOnly? Date(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    static double? FiniteRound(double v, int digits) => double.IsFinite(v) ? Math.Round(v, digits, MidpointRounding.AwayFromZero) : null;
    static int? Valid(double? v, double min, double max) => v is null ? null : v >= min && v <= max ? 1 : 0;
    static string? Normalize(string? s)
    {
        if (s is null) return null;
        var b = new StringBuilder(); bool pending = false;
        foreach (var rune in s.Normalize(NormalizationForm.FormD).ToUpperInvariant().EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (Rune.IsLetterOrDigit(rune)) { if (pending && b.Length > 0) b.Append(' '); b.Append(rune.ToString()); pending = false; }
            else pending = true;
        }
        return b.Length == 0 ? null : b.ToString();
    }
    static bool Word(string s, string word) => Regex.IsMatch(s, @"\b" + word + @"\b", RegexOptions.CultureInvariant);
    static bool StartsWord(string s, string word) => Regex.IsMatch(s, "^" + word + @"\b", RegexOptions.CultureInvariant);
    static string Operator(string? s)
    {
        if (s is null) return "OUTRAS";
        if (Word(s, "TELEFONICA") || Word(s, "VIVO")) return "VIVO";
        if (Word(s, "CLARO") || Word(s, "EMBRATEL")) return "CLARO";
        if (StartsWord(s, "TIM")) return "TIM";
        if (StartsWord(s, "OI") || Word(s, "TELEMAR") || s.Contains("BRASIL TELECOM", StringComparison.Ordinal)) return "OI";
        foreach (var op in new[] { "ALGAR", "SERCOMTEL", "BRISANET" }) if (Word(s, op)) return op;
        return "OUTRAS";
    }
    static string Generation(string? tech) => tech switch { null => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", _ => "OUTRA" };
    static double? Bandwidth(string? s)
    {
        if (s is null || s.Length < 4) return null;
        var p = s.AsSpan(0, 4); int unit = -1; double factor = 0;
        for (int i = 0; i < 4; i++)
        {
            double f = p[i] switch { 'H' => .001, 'K' => 1, 'M' => 1000, 'G' => 1000000, _ => 0 };
            if (f != 0) { if (i == 0 || unit >= 0) return null; unit = i; factor = f; }
            else if (p[i] < '0' || p[i] > '9') return null;
        }
        if (unit < 0) return null;
        double value = 0; for (int i = 0; i < unit; i++) value = value * 10 + p[i] - '0';
        double place = .1; for (int i = unit + 1; i < 4; i++) { value += (p[i] - '0') * place; place /= 10; }
        return value * factor;
    }
}
