using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

try { Etl.Run(); return 0; }
catch (Exception e) { Console.Error.WriteLine(e); return 1; }

static class Etl
{
    static readonly DateOnly Today = new(2026, 10, 15);
    static readonly string[] Ufs = "AC AL AM AP BA CE DF ES GO MA MG MS MT PA PB PE PI PR RJ RN RO RR RS SC SE SP TO".Split(' ');
    static readonly string[] Required = "NumFistel|NumEstacao|NumAto|SiglaUf|CodMunicipio|Municipio.NomeMunicipio|NomeEntidade|EnderecoEstacao|Latitude|Longitude|Tecnologia|DesignacaoEmissao|FreqTxMHz|FreqRxMHz|Azimute|PotenciaTransmissorWatts|GanhoAntena|AlturaAntena|AnguloElevacao|AnguloMeiaPotenciaAntena|FrenteCostaAntena|Polarizacao|CodTipoClasseEstacao|CodTipoAntena|CodEquipamentoAntena|CodEquipamentoTransmissor|Status.state|DataLicenciamento|DataPrimeiroLicenciamento|DataValidade".Split('|');
    const string StationColumns = "fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora";
    const string EmissionColumns = "uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida";
    record struct StationKey(string Uf, string Fistel, string Estacao);
    record struct EmissionKey(string Uf, string Fistel, string Estacao, string? Ato, double? Azimuth, double? Tx, double? Rx, string? Designation, string? Technology);

    public static void Run()
    {
        string root = OperatingSystem.IsWindows() ? @"D:\Projects\TCC" : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string db = Path.Combine(root, "db", "chatgpt_gpt6_completo_r1.db");
        Directory.CreateDirectory(Path.GetDirectoryName(db)!);
        if (File.Exists(db)) throw new IOException($"Banco de saída já existe: {db}");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db }.ToString());
        connection.Open();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;";
            setup.ExecuteNonQuery();
            setup.CommandText = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"));
            setup.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        using var stationInsert = Prepare(connection, transaction, "estacao", StationColumns);
        using var emissionInsert = Prepare(connection, transaction, "emissao", EmissionColumns);
        var stations = new HashSet<StationKey>();
        var emissions = new HashSet<EmissionKey>();
        foreach (string ufFile in Ufs)
        {
            string path = Path.Combine(root, "files", ufFile + ".zip");
            using var zip = ZipFile.OpenRead(path);
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (entries.Length != 1 || !entries[0].Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"ZIP deve conter um CSV: {path}");
            using var reader = new StreamReader(entries[0].Open(), new UTF8Encoding(false, true), true, 65536);
            var csv = new CsvReader(reader);
            var header = csv.ReadRow() ?? throw new InvalidDataException($"CSV vazio: {path}");
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < header.Count; i++) if (!index.TryAdd(header[i].Trim(), i)) throw new InvalidDataException($"Coluna duplicada: {header[i]}");
            foreach (var col in Required) if (!index.ContainsKey(col)) throw new InvalidDataException($"Coluna ausente em {path}: {col}");
            List<string>? row;
            long count = 0;
            while ((row = csv.ReadRow()) != null)
            {
                count++;
                if (row.Count != header.Count) throw new InvalidDataException($"Número de colunas inválido em {path}, registro {count + 1}");
                string? Get(string name) { var s = row[index[name]].Trim(); return s.Length == 0 ? null : s; }
                string? Upper(string name) => Get(name)?.ToUpperInvariant();
                double? Num(string name) => Number(Get(name));
                string? uf = Upper("SiglaUf"), fistel = Get("NumFistel"), estacao = Get("NumEstacao");
                if (uf == null || fistel == null || estacao == null) continue;
                var sk = new StationKey(uf, fistel, estacao);
                if (stations.Add(sk))
                {
                    double? lat = Num("Latitude"), lon = Num("Longitude");
                    string? entity = Normalize(Get("NomeEntidade"));
                    bool valid = lat is >= -33.75 and <= 5.28 && lon is >= -73.99 and <= -28.83 && (lat != 0 || lon != 0);
                    Insert(stationInsert, fistel, estacao, uf, Get("CodMunicipio"), Get("Municipio.NomeMunicipio"), Normalize(Get("EnderecoEstacao")), lat, lon, valid ? 1 : 0, entity, Operator(entity));
                }
                string? ato = Get("NumAto"), tech = Upper("Tecnologia"), designation = Upper("DesignacaoEmissao");
                double? az = Num("Azimute"), tx = Num("FreqTxMHz"), rx = Num("FreqRxMHz");
                if (!emissions.Add(new(uf, fistel, estacao, ato, az, tx, rx, designation, tech))) continue;
                double? power = Num("PotenciaTransmissorWatts"), elevation = Num("AnguloElevacao");
                DateOnly? licensed = Date(Get("DataLicenciamento")), first = Date(Get("DataPrimeiroLicenciamento")), expiry = Date(Get("DataValidade"));
                double? duplex = tx.HasValue && rx.HasValue ? Finite(Math.Round(tx.Value - rx.Value, 3, MidpointRounding.AwayFromZero)) : null;
                double? dbm = power is > 0 ? Math.Round(10 * (Math.Log10(power.Value) + 3), 1, MidpointRounding.AwayFromZero) : null;
                Insert(emissionInsert, uf, fistel, estacao, ato, tech, Generation(tech), designation, Bandwidth(designation), tx, rx, duplex, az, power, Num("GanhoAntena"), Num("AlturaAntena"), DateText(licensed), DateText(first), DateText(expiry), expiry < Today ? 1 : 0, first.HasValue ? Math.Round((Today.DayNumber - first.Value.DayNumber) / 365.25, 1, MidpointRounding.AwayFromZero) : null, Get("Status.state"), Get("CodTipoClasseEstacao"), Upper("Polarizacao"), elevation, Num("AnguloMeiaPotenciaAntena"), Num("FrenteCostaAntena"), Get("CodTipoAntena"), Get("CodEquipamentoAntena"), Get("CodEquipamentoTransmissor"), dbm, Validate(az, 0, 360), Validate(elevation, -90, 90));
            }
            Console.WriteLine($"{ufFile}: {count:N0} registros processados");
        }
        transaction.Commit();
        Console.WriteLine($"Concluído: {stations.Count:N0} estações, {emissions.Count:N0} emissões.");
    }
    static SqliteCommand Prepare(SqliteConnection c, SqliteTransaction t, string table, string columns)
    {
        var names = columns.Split(','); var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = $"INSERT INTO {table} ({columns}) VALUES ({string.Join(',', names.Select((_, i) => "$p" + i))})";
        for (int i = 0; i < names.Length; i++) cmd.Parameters.Add(new SqliteParameter("$p" + i, DBNull.Value));
        cmd.Prepare(); return cmd;
    }
    static void Insert(SqliteCommand cmd, params object?[] values)
    {
        for (int i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
    static double? Number(string? s)
    {
        if (s == null) return null;
        if (double.TryParse(s.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d)) return d;
        return null;
    }
    static double? Finite(double value) => double.IsFinite(value) ? value : null;
    static DateOnly? Date(string? s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    static string? DateText(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static int? Validate(double? value, double min, double max) => value.HasValue ? value >= min && value <= max ? 1 : 0 : null;
    static string? Normalize(string? text)
    {
        if (text == null) return null;
        var sb = new StringBuilder(); bool space = false;
        foreach (var rune in text.Normalize(NormalizationForm.FormD).ToUpperInvariant().EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(rune);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (Rune.IsLetterOrDigit(rune)) { if (space && sb.Length > 0) sb.Append(' '); sb.Append(rune); space = false; }
            else space = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
    static string Operator(string? entity)
    {
        if (entity == null) return "OUTRAS";
        string padded = " " + entity + " ";
        bool Has(string word) => padded.Contains(" " + word + " ", StringComparison.Ordinal);
        bool Starts(string word) => entity == word || entity.StartsWith(word + " ", StringComparison.Ordinal);
        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (Starts("TIM")) return "TIM";
        if (Starts("OI") || Has("TELEMAR") || Has("BRASIL TELECOM")) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }
    static string Generation(string? tech) => tech switch { null => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", _ => "OUTRA" };
    static double? Bandwidth(string? s)
    {
        if (s == null || s.Length < 4) return null;
        int pos = -1; double factor = 0;
        for (int i = 0; i < 4; i++)
        {
            char c = s[i];
            if (c is >= '0' and <= '9') continue;
            if (i == 0 || pos != -1) return null;
            factor = c switch { 'H' => .001, 'K' => 1, 'M' => 1000, 'G' => 1000000, _ => 0 };
            if (factor == 0) return null;
            pos = i;
        }
        if (pos < 1) return null;
        double integer = double.Parse(s[..pos], CultureInfo.InvariantCulture), fraction = 0, scale = 1;
        for (int i = pos + 1; i < 4; i++) { scale *= 10; fraction += (s[i] - '0') / scale; }
        return (integer + fraction) * factor;
    }
}

sealed class CsvReader(TextReader reader)
{
    public List<string>? ReadRow()
    {
        var fields = new List<string>(); var value = new StringBuilder();
        bool quoted = false, closed = false, started = false, any = false;
        while (true)
        {
            int n = reader.Read();
            if (n < 0)
            {
                if (quoted) throw new InvalidDataException("CSV termina dentro de campo entre aspas");
                if (!any && fields.Count == 0 && !started) return null;
                fields.Add(value.ToString()); return fields;
            }
            any = true; char c = (char)n;
            if (quoted)
            {
                if (c == '"') { if (reader.Peek() == '"') { reader.Read(); value.Append('"'); } else { quoted = false; closed = true; } }
                else value.Append(c);
                continue;
            }
            if (c == '|' || c == '\r' || c == '\n')
            {
                fields.Add(value.ToString()); value.Clear(); started = closed = false;
                if (c == '|') continue;
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                return fields;
            }
            if (closed) throw new InvalidDataException("Caracteres após fechamento de aspas no CSV");
            if (c == '"') { if (started) throw new InvalidDataException("Aspas inesperadas no CSV"); quoted = true; started = true; }
            else { value.Append(c); started = true; }
        }
    }
}
