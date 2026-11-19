using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

try { Pipeline.Run(); return 0; }
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }

static class Pipeline
{
    static readonly DateOnly Today = new(2026, 10, 15);
    static readonly string[] Ufs = "AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO".Split(' ');
    static readonly string[] Required = "NumFistel NumEstacao NumAto SiglaUf CodMunicipio Municipio.NomeMunicipio NomeEntidade EnderecoEstacao Latitude Longitude Tecnologia DesignacaoEmissao FreqTxMHz FreqRxMHz Azimute PotenciaTransmissorWatts GanhoAntena AlturaAntena AnguloElevacao AnguloMeiaPotenciaAntena FrenteCostaAntena Polarizacao CodTipoClasseEstacao CodTipoAntena CodEquipamentoAntena CodEquipamentoTransmissor Status.state DataLicenciamento DataPrimeiroLicenciamento DataValidade".Split(' ');
    static readonly string[] StationColumns = "uf fistel num_estacao cod_municipio municipio endereco latitude longitude coordenada_valida entidade operadora".Split(' ');
    static readonly string[] EmissionColumns = "uf fistel num_estacao num_ato tecnologia geracao designacao_emissao largura_banda_khz freq_tx_mhz freq_rx_mhz duplex_mhz azimute potencia_w ganho_antena altura_antena_m data_licenciamento data_primeiro_licenciamento data_validade vencida anos_licenciada status classe_estacao polarizacao angulo_elevacao_graus angulo_meia_potencia_graus frente_costa_db cod_tipo_antena cod_equipamento_antena cod_equipamento_transmissor potencia_dbm azimute_valido elevacao_valida".Split(' ');
    public static void Run()
    {
        var root = FindRoot();
        var path = Path.Combine(root, "db", "chatgpt_gpt_6_completo_r3.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        db.Open();
        Execute(db, "PRAGMA journal_mode=DELETE; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-131072; PRAGMA foreign_keys=ON;");
        Execute(db, File.ReadAllText(Path.Combine(root, "src", "DDL.sql")));
        // Paired null flags preserve distinctions between NULL and any possible value.
        Execute(db, "CREATE UNIQUE INDEX dedup_temporario ON emissao (uf,fistel,num_estacao, num_ato IS NULL,coalesce(num_ato,''), azimute IS NULL,coalesce(azimute,0), freq_tx_mhz IS NULL,coalesce(freq_tx_mhz,0), freq_rx_mhz IS NULL,coalesce(freq_rx_mhz,0), designacao_emissao IS NULL,coalesce(designacao_emissao,''), tecnologia IS NULL,coalesce(tecnologia,''));");
        using var transaction = db.BeginTransaction();
        using var station = Insert(db, transaction, "estacao", StationColumns);
        using var emission = Insert(db, transaction, "emissao", EmissionColumns);
        long read = 0, rejected = 0, stations = 0, emissions = 0;
        foreach (string ufFile in Ufs)
        {
            var zipPath = Path.Combine(root, "files", ufFile + ".zip");
            using var zip = ZipFile.OpenRead(zipPath);
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (entries.Length != 1 || !entries[0].Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"ZIP inválido: {zipPath}");
            using var reader = new StreamReader(entries[0].Open(), new UTF8Encoding(false, true), true, 65536);
            using var rows = Csv(reader).GetEnumerator();
            if (!rows.MoveNext()) throw new InvalidDataException($"CSV vazio: {zipPath}");
            var header = rows.Current.Select((v, i) => (v, i)).ToDictionary(x => x.v.Trim(), x => x.i, StringComparer.Ordinal);
            foreach (var name in Required) if (!header.ContainsKey(name)) throw new InvalidDataException($"Coluna ausente: {name} em {zipPath}");
            while (rows.MoveNext())
            {
                read++;
                var row = rows.Current;
                if (row.Count != header.Count) throw new InvalidDataException($"Quantidade de campos inválida em {zipPath}, registro {read}");
                string? Get(string name) { var value = row[header[name]].Trim(); return value.Length == 0 ? null : value; }
                string? Upper(string name) => Get(name)?.ToUpperInvariant();
                double? Number(string name) => ParseNumber(Get(name));
                string? uf = Upper("SiglaUf"), fistel = Get("NumFistel"), num = Get("NumEstacao");
                if (uf is null || fistel is null || num is null) { rejected++; continue; }
                double? lat = Number("Latitude"), lon = Number("Longitude");
                string? entidade = Normalize(Get("NomeEntidade"));
                object?[] s = [uf, fistel, num, Get("CodMunicipio"), Get("Municipio.NomeMunicipio"), Normalize(Get("EnderecoEstacao")), lat, lon, lat is >= -33.75 and <= 5.28 && lon is >= -73.99 and <= -28.83 && (lat != 0 || lon != 0) ? 1 : 0, entidade, Operator(entidade)];
                stations += Bind(station, s);
                string? tech = Upper("Tecnologia"), designation = Upper("DesignacaoEmissao");
                double? tx = Number("FreqTxMHz"), rx = Number("FreqRxMHz"), az = Number("Azimute"), power = Number("PotenciaTransmissorWatts"), elevation = Number("AnguloElevacao");
                DateOnly? licensing = Date(Get("DataLicenciamento")), first = Date(Get("DataPrimeiroLicenciamento")), expiration = Date(Get("DataValidade"));
                object?[] e = [uf, fistel, num, Get("NumAto"), tech, Generation(tech), designation, Bandwidth(designation), tx, rx, tx.HasValue && rx.HasValue ? RoundFinite(tx.Value-rx.Value, 3) : null, az, power, Number("GanhoAntena"), Number("AlturaAntena"), Format(licensing), Format(first), Format(expiration), expiration.HasValue && expiration.Value < Today ? 1 : 0, first.HasValue ? Math.Round((Today.DayNumber-first.Value.DayNumber)/365.25, 1, MidpointRounding.AwayFromZero) : null, Get("Status.state"), Get("CodTipoClasseEstacao"), Upper("Polarizacao"), elevation, Number("AnguloMeiaPotenciaAntena"), Number("FrenteCostaAntena"), Get("CodTipoAntena"), Get("CodEquipamentoAntena"), Get("CodEquipamentoTransmissor"), power is > 0 ? RoundFinite(10*(Math.Log10(power.Value)+3), 1) : null, Valid(az,0,360), Valid(elevation,-90,90)];
                emissions += Bind(emission, e);
            }
            Console.WriteLine($"{ufFile}: {read:N0} linhas lidas; {stations:N0} estações e {emissions:N0} emissões acumuladas");
        }
        transaction.Commit();
        Execute(db, "DROP INDEX dedup_temporario;");
        Console.WriteLine($"Concluído: {read} linhas, {rejected} rejeitadas, {stations} estações, {emissions} emissões.");
    }
    static string FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "src", "DDL.sql")) && Directory.Exists(Path.Combine(d.FullName, "files"))) return d.FullName;
        throw new DirectoryNotFoundException("Raiz do projeto com src/DDL.sql e files não encontrada.");
    }
    static void Execute(SqliteConnection db, string sql) { using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    static SqliteCommand Insert(SqliteConnection db, SqliteTransaction tx, string table, string[] columns)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = $"INSERT OR IGNORE INTO {table} ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select((_, i) => "@p"+i))})";
        for (int i=0; i<columns.Length; i++) cmd.Parameters.AddWithValue("@p"+i, DBNull.Value);
        cmd.Prepare(); return cmd;
    }
    static int Bind(SqliteCommand cmd, object?[] values) { for (int i=0; i<values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value; return cmd.ExecuteNonQuery(); }
    static double? ParseNumber(string? s)
    {
        if (s is null) return null;
        s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : null;
    }
    static DateOnly? Date(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    static string? Format(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static double? RoundFinite(double d, int decimals) => double.IsFinite(d) ? Math.Round(d, decimals, MidpointRounding.AwayFromZero) : null;
    static int? Valid(double? n, double min, double max) => n is null ? null : n >= min && n <= max ? 1 : 0;
    static string? Normalize(string? s)
    {
        if (s is null) return null;
        var b = new StringBuilder(); bool space = false;
        foreach (var rune in s.Normalize(NormalizationForm.FormD).ToUpperInvariant().EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (Rune.IsLetterOrDigit(rune)) { if (space && b.Length > 0) b.Append(' '); b.Append(rune); space = false; }
            else space = true;
        }
        return b.Length == 0 ? null : b.ToString();
    }
    static string Operator(string? s)
    {
        if (s is null) return "OUTRAS";
        var words = s.Split(' ');
        bool Has(string w) => Array.IndexOf(words, w) >= 0;
        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (words[0] == "TIM") return "TIM";
        if (words[0] == "OI" || Has("TELEMAR") || (" " + s + " ").Contains(" BRASIL TELECOM ", StringComparison.Ordinal)) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }
    static string Generation(string? s) => s switch { null => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", _ => "OUTRA" };
    static double? Bandwidth(string? s)
    {
        if (s is null || s.Length < 4) return null;
        int unit = -1; double factor = 0;
        for (int i=0; i<4; i++)
        {
            char c = s[i];
            if (c is >= '0' and <= '9') continue;
            if (i == 0 || unit != -1) return null;
            factor = c switch { 'H' => .001, 'K' => 1, 'M' => 1000, 'G' => 1000000, _ => 0 };
            if (factor == 0) return null;
            unit = i;
        }
        if (unit < 0) return null;
        double integer = double.Parse(s.AsSpan(0,unit), CultureInfo.InvariantCulture), fraction = 0, divisor = 1;
        for (int i=unit+1; i<4; i++) { fraction = fraction*10 + (s[i]-'0'); divisor *= 10; }
        return (integer + fraction/divisor)*factor;
    }
    // Parser accepts quoted delimiters, escaped quotes and embedded line breaks.
    static IEnumerable<List<string>> Csv(TextReader reader)
    {
        var row = new List<string>(); var field = new StringBuilder(); bool quoted = false, afterQuote = false, started = false;
        while (true)
        {
            int x = reader.Read();
            if (x < 0)
            {
                if (quoted) throw new InvalidDataException("CSV com aspas não fechadas.");
                if (started || row.Count > 0 || field.Length > 0) { row.Add(field.ToString()); yield return row; }
                yield break;
            }
            char c = (char)x;
            if (quoted)
            {
                if (c == '"') { if (reader.Peek() == '"') { reader.Read(); field.Append('"'); } else { quoted = false; afterQuote = true; } }
                else field.Append(c);
                continue;
            }
            if (c == '"' && !started && field.Length == 0) { quoted = true; started = true; continue; }
            if (c == '|' || c == '\r' || c == '\n')
            {
                row.Add(field.ToString()); field.Clear(); started = false; afterQuote = false;
                if (c != '|') { if (c == '\r' && reader.Peek() == '\n') reader.Read(); yield return row; row = new List<string>(); }
                continue;
            }
            if (afterQuote || c == '"') throw new InvalidDataException("CSV com aspas inválidas.");
            started = true; field.Append(c);
        }
    }
}
