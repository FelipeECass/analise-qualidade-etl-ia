using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

try { Run(); return 0; }
catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }

static void Run()
{
    // The executable is under src/<project>/bin/<configuration>/<framework>.
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    string input = Path.Combine(root, "files"), ddl = Path.Combine(root, "src", "DDL.sql");
    string output = Path.Combine(root, "db", "chatgpt_gpt6_zeroshot_r1.db");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    if (File.Exists(output)) throw new IOException($"O banco de saída já existe: {output}");
    using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = output, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
    db.Open();
    using (var setup = db.CreateCommand())
    {
        setup.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;";
        setup.ExecuteNonQuery();
        setup.CommandText = File.ReadAllText(ddl); setup.ExecuteNonQuery();
        setup.CommandText = "CREATE TEMP TABLE dedup (uf TEXT, fistel TEXT, est TEXT, ato TEXT, az REAL, tx REAL, rx REAL, des TEXT, tec TEXT); CREATE UNIQUE INDEX dedup_key ON dedup (uf, fistel, est, coalesce(ato, char(0)), coalesce(az, char(0)), coalesce(tx, char(0)), coalesce(rx, char(0)), coalesce(des, char(0)), coalesce(tec, char(0)));";
        setup.ExecuteNonQuery();
    }
    using var transaction = db.BeginTransaction();
    using var station = Command(db, "INSERT OR IGNORE INTO estacao (uf,fistel,num_estacao,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES (" + Args(11) + ")", 11);
    using var unique = Command(db, "INSERT OR IGNORE INTO dedup VALUES (" + Args(9) + ")", 9);
    using var emission = Command(db, "INSERT INTO emissao (uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida) VALUES (" + Args(32) + ")", 32);
    string[] ufs = "AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO".Split(' ');
    foreach (string ufFile in ufs)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(input, ufFile + ".zip"));
        var entry = zip.Entries.Single(e => !string.IsNullOrEmpty(e.Name));
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true, 65536);
        var csv = new Csv(reader);
        var headers = csv.Read() ?? throw new InvalidDataException($"CSV vazio: {ufFile}");
        var map = headers.Select((name, i) => (name: name.Trim().TrimStart('\uFEFF'), i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
        string[] fields = "NumFistel|NumEstacao|SiglaUf|NumAto|CodMunicipio|Municipio.NomeMunicipio|NomeEntidade|EnderecoEstacao|Latitude|Longitude|Tecnologia|DesignacaoEmissao|FreqTxMHz|FreqRxMHz|Azimute|PotenciaTransmissorWatts|GanhoAntena|AlturaAntena|AnguloElevacao|AnguloMeiaPotenciaAntena|FrenteCostaAntena|Polarizacao|CodTipoClasseEstacao|CodTipoAntena|CodEquipamentoAntena|CodEquipamentoTransmissor|Status.state|DataLicenciamento|DataPrimeiroLicenciamento|DataValidade".Split('|');
        int[] positions = fields.Select(f => map.TryGetValue(f, out int i) ? i : throw new InvalidDataException($"Coluna ausente em {ufFile}: {f}")).ToArray();
        List<string>? row;
        while ((row = csv.Read()) != null)
        {
            if (row.Count != headers.Count) throw new InvalidDataException($"Número de campos incorreto em {ufFile}");
            string? V(int n) { string s = row[positions[n]].Trim(); return s.Length == 0 ? null : s; }
            string? fistel = V(0), est = V(1), uf = V(2)?.ToUpperInvariant();
            if (fistel == null || est == null || uf == null) continue;
            string? ato = V(3), entity = Normalize(V(6)), address = Normalize(V(7));
            double? lat = Number(V(8)), lon = Number(V(9));
            bool coord = lat is >= -33.75 and <= 5.28 && lon is >= -73.99 and <= -28.83 && (lat != 0 || lon != 0);
            Execute(station, uf, fistel, est, V(4), V(5), address, lat, lon, coord ? 1 : 0, entity, Operator(entity));
            string? tech = V(10)?.ToUpperInvariant(), design = V(11)?.ToUpperInvariant();
            double? tx = Number(V(12)), rx = Number(V(13)), az = Number(V(14));
            if (Execute(unique, uf, fistel, est, ato, az, tx, rx, design, tech) == 0) continue;
            double? power = Number(V(15)), elevation = Number(V(18));
            string? d1 = Date(V(27)), d2 = Date(V(28)), d3 = Date(V(29));
            var today = new DateTime(2026, 10, 15);
            double? years = d2 == null ? null : Round((today - DateTime.ParseExact(d2, "yyyy-MM-dd", CultureInfo.InvariantCulture)).TotalDays / 365.25, 1);
            double? dbm = power > 0 ? Round(10 * (Math.Log10(power.Value) + 3), 1) : null;
            double? duplex = tx.HasValue && rx.HasValue && double.IsFinite(tx.Value - rx.Value) ? Round(tx.Value - rx.Value, 3) : null;
            Execute(emission, uf, fistel, est, ato, tech, Generation(tech), design, Bandwidth(design), tx, rx, duplex, az, power, Number(V(16)), Number(V(17)), d1, d2, d3, d3 != null && string.CompareOrdinal(d3, "2026-10-15") < 0 ? 1 : 0, years, V(26), V(22), V(21)?.ToUpperInvariant(), elevation, Number(V(19)), Number(V(20)), V(23), V(24), V(25), dbm, Valid(az, 0, 360), Valid(elevation, -90, 90));
        }
        Console.WriteLine($"{ufFile} concluído");
    }
    transaction.Commit();
}
static string Args(int count) => string.Join(',', Enumerable.Range(0, count).Select(i => "$p" + i));
static SqliteCommand Command(SqliteConnection db, string sql, int count)
{
    var cmd = db.CreateCommand(); cmd.CommandText = sql;
    for (int i = 0; i < count; i++) cmd.Parameters.AddWithValue("$p" + i, DBNull.Value);
    cmd.Prepare(); return cmd;
}
static int Execute(SqliteCommand cmd, params object?[] values)
{
    for (int i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
    return cmd.ExecuteNonQuery();
}
static double? Number(string? s)
{
    if (s == null) return null;
    return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : null;
}
static string? Date(string? s) => s != null && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
static double Round(double n, int digits) => Math.Round(n, digits, MidpointRounding.AwayFromZero);
static int? Valid(double? n, double min, double max) => n.HasValue ? n >= min && n <= max ? 1 : 0 : null;
static string? Normalize(string? s)
{
    if (s == null) return null;
    var b = new StringBuilder(); bool space = false;
    foreach (var r in s.Normalize(NormalizationForm.FormD).EnumerateRunes())
    {
        var category = Rune.GetUnicodeCategory(r);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
        if (Rune.IsLetterOrDigit(r)) { if (space && b.Length > 0) b.Append(' '); b.Append(Rune.ToUpperInvariant(r)); space = false; }
        else space = true;
    }
    return b.Length == 0 ? null : b.ToString();
}
static string Operator(string? s)
{
    if (s == null) return "OUTRAS";
    if (Regex.IsMatch(s, @"\b(TELEFONICA|VIVO)\b")) return "VIVO";
    if (Regex.IsMatch(s, @"\b(CLARO|EMBRATEL)\b")) return "CLARO";
    if (Regex.IsMatch(s, @"^TIM\b")) return "TIM";
    if (Regex.IsMatch(s, @"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b")) return "OI";
    foreach (var name in new[] { "ALGAR", "SERCOMTEL", "BRISANET" }) if (Regex.IsMatch(s, @"\b" + name + @"\b")) return name;
    return "OUTRAS";
}
static string Generation(string? s) => s switch { null => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", _ => "OUTRA" };
static double? Bandwidth(string? s)
{
    if (s == null || s.Length < 4) return null;
    string p = s[..4]; int unit = -1;
    for (int i = 0; i < 4; i++)
        if ("HKMG".Contains(p[i])) { if (unit != -1 || i == 0) return null; unit = i; }
        else if (p[i] is < '0' or > '9') return null;
    if (unit < 0) return null;
    string digits = p.Remove(unit, 1);
    double value = int.Parse(digits, CultureInfo.InvariantCulture) / Math.Pow(10, 3 - unit);
    return value * (p[unit] switch { 'H' => 0.001, 'K' => 1, 'M' => 1000, _ => 1000000 });
}

sealed class Csv(StreamReader reader)
{
    public List<string>? Read()
    {
        var fields = new List<string>(); var field = new StringBuilder(); bool quoted = false, started = false;
        while (true)
        {
            int c = reader.Read();
            if (c < 0) { if (quoted) throw new InvalidDataException("Campo CSV com aspas não fechadas"); if (!started && fields.Count == 0 && field.Length == 0) return null; fields.Add(field.ToString()); return fields; }
            started = true;
            if (quoted)
            {
                if (c == '"') { if (reader.Peek() == '"') { reader.Read(); field.Append('"'); } else quoted = false; }
                else field.Append((char)c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == '|') { fields.Add(field.ToString()); field.Clear(); }
            else if (c == '\r' || c == '\n') { if (c == '\r' && reader.Peek() == '\n') reader.Read(); fields.Add(field.ToString()); return fields; }
            else field.Append((char)c);
        }
    }
}
