using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;

try { Etl.Run(); return 0; }
catch (Exception e) { Console.Error.WriteLine(e); return 1; }

static class Etl
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly DateOnly Today = new(2026, 10, 15);
    static readonly string[] Ufs = "AC AL AP AM BA CE DF ES GO MA MT MS MG PA PB PR PE PI RJ RN RS RO RR SC SP SE TO".Split(' ');
    static readonly string[] Fields = "NumFistel|NumEstacao|NumAto|SiglaUf|CodMunicipio|Municipio.NomeMunicipio|NomeEntidade|EnderecoEstacao|Latitude|Longitude|Tecnologia|DesignacaoEmissao|FreqTxMHz|FreqRxMHz|Azimute|PotenciaTransmissorWatts|GanhoAntena|AlturaAntena|AnguloElevacao|AnguloMeiaPotenciaAntena|FrenteCostaAntena|Polarizacao|CodTipoClasseEstacao|CodTipoAntena|CodEquipamentoAntena|CodEquipamentoTransmissor|Status.state|DataLicenciamento|DataPrimeiroLicenciamento|DataValidade".Split('|');
    static string? Clean(string? s) { s = s?.Trim(); return string.IsNullOrEmpty(s) ? null : s; }
    static string? Upper(string? s) => s?.ToUpperInvariant();
    static double? Number(string? s) => s != null && double.TryParse(s.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, Inv, out double n) && double.IsFinite(n) ? n : null;
    static DateOnly? Date(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : null;
    static string? DateText(DateOnly? d) => d?.ToString("yyyy-MM-dd", Inv);
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
    static string Operator(string? s)
    {
        if (s == null) return "OUTRAS";
        var words = s.Split(' ');
        bool Has(string w) => Array.IndexOf(words, w) >= 0;
        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (words[0] == "TIM") return "TIM";
        if (words[0] == "OI" || Has("TELEMAR") || s.Contains("BRASIL TELECOM", StringComparison.Ordinal)) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }
    static double? Band(string? s)
    {
        if (s == null || s.Length < 4) return null;
        var t = s.AsSpan(0, 4); int p = -1; double factor = 0;
        for (int i = 0; i < 4; i++)
        {
            double f = t[i] switch { 'H' => .001, 'K' => 1, 'M' => 1000, 'G' => 1000000, _ => 0 };
            if (f != 0) { if (p >= 0 || i == 0) return null; p = i; factor = f; }
            else if (t[i] < '0' || t[i] > '9') return null;
        }
        if (p < 0) return null;
        double number = 0;
        for (int i = 0; i < 4; i++) if (i != p) number = number * 10 + t[i] - '0';
        return number / Math.Pow(10, 3 - p) * factor;
    }
    static double Round(double x, int n) => Math.Round(x, n, MidpointRounding.AwayFromZero);
    static SqliteCommand Insert(SqliteConnection db, SqliteTransaction tx, string table, string columns)
    {
        var names = columns.Split(','); var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = $"INSERT OR IGNORE INTO {table} ({columns}) VALUES ({string.Join(',', names.Select((_, i) => "$p" + i))})";
        for (int i = 0; i < names.Length; i++) cmd.Parameters.Add(new SqliteParameter("$p" + i, DBNull.Value));
        cmd.Prepare(); return cmd;
    }
    static int Execute(SqliteCommand cmd, params object?[] values)
    {
        for (int i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        return cmd.ExecuteNonQuery();
    }
    public static void Run()
    {
        // On Windows the specified root is fixed; on other platforms the project is under the workspace root.
        string root = OperatingSystem.IsWindows() ? @"D:\Projects\TCC" : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string output = Path.Combine(root, "db", "chatgpt_gpt6_zeroshot_r3.db");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = output, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        db.Open();
        using (var ddl = db.CreateCommand()) { ddl.CommandText = File.ReadAllText(Path.Combine(root, "src", "DDL.sql")); ddl.ExecuteNonQuery(); }
        using (var pragma = db.CreateCommand()) { pragma.CommandText = "PRAGMA synchronous=OFF; PRAGMA journal_mode=MEMORY; PRAGMA cache_size=-32768;"; pragma.ExecuteNonQuery(); }
        using var tx = db.BeginTransaction();
        using (var setup = db.CreateCommand()) { setup.Transaction = tx; setup.CommandText = "CREATE TABLE _etl_chave (uf TEXT NOT NULL, fistel TEXT NOT NULL, estacao TEXT NOT NULL, ato TEXT, az REAL, tx REAL, rx REAL, design TEXT, tech TEXT); CREATE UNIQUE INDEX _etl_chave_indice ON _etl_chave (uf, fistel, estacao, ifnull(ato,''), ifnull(az,0), az IS NULL, ifnull(tx,0), tx IS NULL, ifnull(rx,0), rx IS NULL, ifnull(design,''), ifnull(tech,''));"; setup.ExecuteNonQuery(); }
        using var keyCmd = Insert(db, tx, "_etl_chave", "uf,fistel,estacao,ato,az,tx,rx,design,tech");
        using var stationCmd = Insert(db, tx, "estacao", "fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora");
        using var emissionCmd = Insert(db, tx, "emissao", "uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida");
        foreach (string ufFile in Ufs)
        {
            // Keys cannot repeat across different UF values; retain all keys if files contain another UF.
            ProcessFile(Path.Combine(root, "files", ufFile + ".zip"), stationCmd, emissionCmd, keyCmd);
        }
        using (var cleanup = db.CreateCommand()) { cleanup.Transaction = tx; cleanup.CommandText = "DROP TABLE _etl_chave"; cleanup.ExecuteNonQuery(); }
        tx.Commit();
    }
    static void ProcessFile(string path, SqliteCommand stationCmd, SqliteCommand emissionCmd, SqliteCommand keyCmd)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count != 1) throw new InvalidDataException($"ZIP deve conter um arquivo: {path}");
        using var stream = zip.Entries[0].Open();
        using var parser = new TextFieldParser(stream, Encoding.UTF8) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters("|");
        var header = parser.ReadFields() ?? throw new InvalidDataException($"Cabeçalho ausente: {path}");
        var index = Fields.Select(f => { int n = Array.IndexOf(header, f); return n >= 0 ? n : throw new InvalidDataException($"Coluna ausente: {f} em {path}"); }).ToArray();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields(); if (row == null) continue;
            if (row.Length != header.Length) throw new InvalidDataException($"Linha com número incorreto de campos: {path}, linha {parser.LineNumber}");
            string? V(int i) => Clean(row[index[i]]);
            string? fistel = V(0), station = V(1), ato = V(2), uf = Upper(V(3));
            if (fistel == null || station == null || uf == null) continue;
            {
                double? lat = Number(V(8)), lon = Number(V(9));
                bool valid = lat is >= -33.75 and <= 5.28 && lon is >= -73.99 and <= -28.83 && (lat != 0 || lon != 0);
                string? entity = Normalize(V(6));
                Execute(stationCmd, fistel, station, uf, V(4), V(5), Normalize(V(7)), lat, lon, valid ? 1 : 0, entity, Operator(entity));
            }
            string? tech = Upper(V(10)), design = Upper(V(11));
            double? tx = Number(V(12)), rx = Number(V(13)), az = Number(V(14));
            if (Execute(keyCmd, uf, fistel, station, ato, az, tx, rx, design, tech) == 0) continue;
            double? power = Number(V(15)), elevation = Number(V(18));
            DateOnly? licensed = Date(V(27)), first = Date(V(28)), expires = Date(V(29));
            string generation = tech switch { null => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", _ => "OUTRA" };
            Execute(emissionCmd, uf, fistel, station, ato, tech, generation, design, Band(design), tx, rx,
                tx.HasValue && rx.HasValue ? Round(tx.Value - rx.Value, 3) : null, az, power, Number(V(16)), Number(V(17)),
                DateText(licensed), DateText(first), DateText(expires), expires.HasValue && expires.Value < Today ? 1 : 0,
                first.HasValue ? Round((Today.DayNumber - first.Value.DayNumber) / 365.25, 1) : null,
                V(26), V(22), Upper(V(21)), elevation, Number(V(19)), Number(V(20)), V(23), V(24), V(25),
                power > 0 ? Round(10 * (Math.Log10(power.Value) + 3), 1) : null,
                az.HasValue ? (az >= 0 && az <= 360 ? 1 : 0) : null,
                elevation.HasValue ? (elevation >= -90 && elevation <= 90 ? 1 : 0) : null);
        }
        Console.WriteLine($"{Path.GetFileName(path)} processado");
    }
}
