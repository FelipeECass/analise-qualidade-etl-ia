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
    static readonly string[] Required = "NumFistel|NumEstacao|NumAto|SiglaUf|CodMunicipio|Municipio.NomeMunicipio|NomeEntidade|EnderecoEstacao|Latitude|Longitude|Tecnologia|DesignacaoEmissao|FreqTxMHz|FreqRxMHz|Azimute|PotenciaTransmissorWatts|GanhoAntena|AlturaAntena|AnguloElevacao|AnguloMeiaPotenciaAntena|FrenteCostaAntena|Polarizacao|CodTipoClasseEstacao|CodTipoAntena|CodEquipamentoAntena|CodEquipamentoTransmissor|Status.state|DataLicenciamento|DataPrimeiroLicenciamento|DataValidade".Split('|');
    static readonly Regex Band = new(@"^([0-9]+)([HKMG])([0-9]*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex Operators = new(@"\b(TELEFONICA|VIVO|CLARO|EMBRATEL|TELEMAR|ALGAR|SERCOMTEL|BRISANET)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static void Run()
    {
        string root = OperatingSystem.IsWindows() ? @"D:\Projects\TCC" : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string dbPath = Path.Combine(root, "db", "chatgpt_gpt6_zeroshot_r2.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        db.Open();
        Execute(db, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=FILE; PRAGMA foreign_keys=ON;");
        Execute(db, File.ReadAllText(Path.Combine(root, "src", "DDL.sql")));
        Execute(db, "CREATE TEMP TABLE seen (uf TEXT, fistel TEXT, station TEXT, ato TEXT, az REAL, tx REAL, rx REAL, des TEXT, tech TEXT); CREATE UNIQUE INDEX temp.seen_key ON seen (uf,fistel,station,coalesce(ato,x'00'),coalesce(az,x'00'),coalesce(tx,x'00'),coalesce(rx,x'00'),coalesce(des,x'00'),coalesce(tech,x'00'));");
        using var tx = db.BeginTransaction();
        using var station = Prepare(db, "INSERT OR IGNORE INTO estacao (uf,fistel,num_estacao,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES (" + Params(11) + ")",11);
        using var seen = Prepare(db, "INSERT OR IGNORE INTO seen VALUES (" + Params(9) + ")",9);
        string cols = "uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida";
        using var emission = Prepare(db, $"INSERT INTO emissao ({cols}) VALUES ({Params(32)})",32);
        foreach (string ufFile in Ufs)
        {
            Console.WriteLine($"Processando {ufFile}");
            using var zip = ZipFile.OpenRead(Path.Combine(root, "files", ufFile + ".zip"));
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (entries.Length != 1 || !entries[0].Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"ZIP inválido: {ufFile}");
            using var stream = entries[0].Open();
            using var parser = new TextFieldParser(stream, Encoding.UTF8, true) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            parser.SetDelimiters("|");
            var header = parser.ReadFields() ?? throw new InvalidDataException($"Cabeçalho ausente: {ufFile}");
            var indexes = Required.Select(n => Array.IndexOf(header, n)).ToArray();
            if (indexes.Any(i => i < 0)) throw new InvalidDataException($"Colunas ausentes em {ufFile}: {string.Join(", ", Required.Where((_, i) => indexes[i] < 0))}");
            while (!parser.EndOfData)
            {
                var row = parser.ReadFields(); if (row == null) continue;
                if (indexes.Any(i => i >= row.Length)) throw new InvalidDataException($"Linha incompleta em {ufFile}");
                string? Get(int i) { string v = row[indexes[i]].Trim(); return v.Length == 0 ? null : v; }
                string? fistel = Get(0), number = Get(1), uf = Get(3)?.ToUpperInvariant();
                if (fistel == null || number == null || uf == null) continue;
                string? ato = Get(2), tech = Get(10)?.ToUpperInvariant(), des = Get(11)?.ToUpperInvariant();
                double? lat = Num(Get(8)), lon = Num(Get(9)), txFreq = Num(Get(12)), rxFreq = Num(Get(13)), az = Num(Get(14));
                double? power = Num(Get(15)), elevation = Num(Get(18));
                string? entity = Normalize(Get(6));
                bool coord = lat is >= -33.75 and <= 5.28 && lon is >= -73.99 and <= -28.83 && !(lat == 0 && lon == 0);
                Bind(station, uf, fistel, number, Get(4), Get(5), Normalize(Get(7)), lat, lon, coord ? 1 : 0, entity, Operator(entity));
                station.ExecuteNonQuery();
                Bind(seen, uf, fistel, number, ato, az, txFreq, rxFreq, des, tech);
                if (seen.ExecuteNonQuery() == 0) continue;
                DateOnly? lic = Date(Get(27)), first = Date(Get(28)), expiry = Date(Get(29));
                double? duplex = txFreq.HasValue && rxFreq.HasValue ? Round(txFreq.Value - rxFreq.Value, 3) : null;
                double? dbm = power > 0 ? Round(10 * (Math.Log10(power.Value) + 3), 1) : null;
                double? years = first.HasValue ? Round((Today.DayNumber - first.Value.DayNumber) / 365.25, 1) : null;
                Bind(emission, uf, fistel, number, ato, tech, Generation(tech), des, Bandwidth(des), txFreq, rxFreq, duplex, az, power, Num(Get(16)), Num(Get(17)), Format(lic), Format(first), Format(expiry), expiry < Today ? 1 : 0, years, Get(26), Get(22), Get(21)?.ToUpperInvariant(), elevation, Num(Get(19)), Num(Get(20)), Get(23), Get(24), Get(25), dbm, Valid(az, 0, 360), Valid(elevation, -90, 90));
                emission.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }
    static void Execute(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
    static string Params(int n) => string.Join(',', Enumerable.Range(0,n).Select(i => "$p" + i));
    static SqliteCommand Prepare(SqliteConnection db, string sql, int count) { var c = db.CreateCommand(); c.CommandText = sql; for (int i=0;i<count;i++) c.Parameters.AddWithValue("$p"+i, DBNull.Value); c.Prepare(); return c; }
    static void Bind(SqliteCommand command, params object?[] values) { for(int i=0;i<values.Length;i++) command.Parameters[i].Value = values[i] ?? DBNull.Value; }
    static double? Num(string? s) => s != null && double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;
    static DateOnly? Date(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    static string? Format(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static double Round(double v, int places) => Math.Round(v, places, MidpointRounding.AwayFromZero);
    static int? Valid(double? v, double min, double max) => v.HasValue ? v >= min && v <= max ? 1 : 0 : null;
    static string? Normalize(string? s)
    {
        if (s == null) return null;
        var b = new StringBuilder(); bool space = false;
        foreach (var c in s.Normalize(NormalizationForm.FormD).ToUpperInvariant())
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (char.IsLetterOrDigit(c)) { if (space && b.Length > 0) b.Append(' '); b.Append(c); space = false; }
            else space = true;
        }
        return b.Length == 0 ? null : b.ToString();
    }
    static string Operator(string? entity)
    {
        if (entity == null) return "OUTRAS";
        var words = Operators.Matches(entity).Select(m => m.Value).ToHashSet();
        if (words.Contains("TELEFONICA") || words.Contains("VIVO")) return "VIVO";
        if (words.Contains("CLARO") || words.Contains("EMBRATEL")) return "CLARO";
        if (entity == "TIM" || entity.StartsWith("TIM ", StringComparison.Ordinal)) return "TIM";
        if (entity == "OI" || entity.StartsWith("OI ", StringComparison.Ordinal) || words.Contains("TELEMAR") || (" " + entity + " ").Contains(" BRASIL TELECOM ", StringComparison.Ordinal)) return "OI";
        foreach (var op in new[] { "ALGAR", "SERCOMTEL", "BRISANET" }) if (words.Contains(op)) return op;
        return "OUTRAS";
    }
    static string Generation(string? tech) => tech switch { null => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", _ => "OUTRA" };
    static double? Bandwidth(string? des)
    {
        if (des == null || des.Length < 4) return null;
        var m = Band.Match(des[..4]); if (!m.Success) return null;
        var s = m.Groups[1].Value + (m.Groups[3].Length > 0 ? "." + m.Groups[3].Value : "");
        var factor = m.Groups[2].Value switch { "H" => .001, "K" => 1d, "M" => 1000d, "G" => 1000000d, _ => 0d };
        return double.Parse(s, CultureInfo.InvariantCulture) * factor;
    }
}
