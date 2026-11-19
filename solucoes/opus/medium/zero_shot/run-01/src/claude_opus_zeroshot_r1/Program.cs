using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace ClaudeOpusZeroshotR1;

public static class Program
{
    private const string DbName = "claude_opus_zeroshot_r1.db";
    private static readonly DateTime Today = new(2026, 10, 15);

    public static int Main()
    {
        try
        {
            string root = ResolveRoot();
            string filesDir = Path.Combine(root, "files");
            string ddlPath = Path.Combine(root, "src", "DDL.sql");
            string dbDir = Path.Combine(root, "db");
            Directory.CreateDirectory(dbDir);
            string dbPath = Path.Combine(dbDir, DbName);
            if (File.Exists(dbPath)) File.Delete(dbPath);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var etl = new Etl(dbPath, File.ReadAllText(ddlPath, Encoding.UTF8));
            var zips = Directory.GetFiles(filesDir, "*.zip")
                .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).ToList();
            if (zips.Count == 0) throw new InvalidOperationException($"Nenhum ZIP encontrado em {filesDir}");
            etl.Run(zips);
            Console.WriteLine($"Concluído em {sw.Elapsed}. Banco: {dbPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
    }

    private static string ResolveRoot()
    {
        const string winRoot = @"D:\Projects\TCC";
        if (IsRoot(winRoot)) return winRoot;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (IsRoot(dir.FullName)) return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException(@"Pasta raiz (D:\Projects\TCC) com 'files' e 'src\DDL.sql' não encontrada.");
    }

    private static bool IsRoot(string p) =>
        Directory.Exists(Path.Combine(p, "files")) && File.Exists(Path.Combine(p, "src", "DDL.sql"));

    internal static DateTime TodayRef => Today;
}

internal sealed class Etl
{
    private readonly string _dbPath;
    private readonly string _ddl;
    private readonly HashSet<string> _stations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _emissions = new(StringComparer.Ordinal);
    private long _read, _rejected, _dups, _loaded;

    private static readonly string[] EmissionCols =
    {
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
        "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
        "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
        "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
        "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
        "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
        "elevacao_valida"
    };

    private static readonly string[] StationCols =
    {
        "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude",
        "longitude", "coordenada_valida", "entidade", "operadora"
    };

    public Etl(string dbPath, string ddl)
    {
        _dbPath = dbPath;
        _ddl = ddl;
    }

    public void Run(IEnumerable<string> zips)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        conn.Open();
        Exec(conn, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-200000;");
        Exec(conn, _ddl);

        using var tx = conn.BeginTransaction();
        using var insSt = Prepare(conn, tx, "estacao", StationCols);
        using var insEm = Prepare(conn, tx, "emissao", EmissionCols);

        foreach (var zip in zips)
        {
            long before = _loaded;
            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        ?? archive.Entries.FirstOrDefault(e => e.Length > 0)
                        ?? throw new InvalidDataException($"ZIP sem CSV: {zip}");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);
            ProcessFile(reader, insSt, insEm, zip);
            Console.WriteLine($"{Path.GetFileName(zip)}: {_loaded - before} emissões carregadas");
        }

        tx.Commit();
        Console.WriteLine($"Linhas lidas: {_read}; rejeitadas: {_rejected}; duplicadas: {_dups}; emissões: {_loaded}; estações: {_stations.Count}");
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Prepare(SqliteConnection conn, SqliteTransaction tx, string table, string[] cols)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select(c => "@" + c))})";
        foreach (var c in cols) cmd.Parameters.Add(new SqliteParameter("@" + c, DBNull.Value));
        cmd.Prepare();
        return cmd;
    }

    private void ProcessFile(TextReader reader, SqliteCommand insSt, SqliteCommand insEm, string zip)
    {
        var csv = new PipeCsvReader(reader);
        var header = csv.ReadRecord() ?? throw new InvalidDataException($"CSV vazio em {zip}");
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            var h = header[i].Trim().TrimStart('\uFEFF');
            idx.TryAdd(h, i);
        }

        int Col(string name) => idx.TryGetValue(name, out var i) ? i : -1;
        int cFistel = Col("NumFistel"), cEstacao = Col("NumEstacao"), cAto = Col("NumAto"), cUf = Col("SiglaUf"),
            cCodMun = Col("CodMunicipio"), cMun = Col("Municipio.NomeMunicipio"), cEnt = Col("NomeEntidade"),
            cEnd = Col("EnderecoEstacao"), cLat = Col("Latitude"), cLon = Col("Longitude"), cTec = Col("Tecnologia"),
            cDesig = Col("DesignacaoEmissao"), cTx = Col("FreqTxMHz"), cRx = Col("FreqRxMHz"), cAz = Col("Azimute"),
            cPot = Col("PotenciaTransmissorWatts"), cGanho = Col("GanhoAntena"), cAlt = Col("AlturaAntena"),
            cElev = Col("AnguloElevacao"), cMeia = Col("AnguloMeiaPotenciaAntena"), cFc = Col("FrenteCostaAntena"),
            cPol = Col("Polarizacao"), cClasse = Col("CodTipoClasseEstacao"), cTipoAnt = Col("CodTipoAntena"),
            cEqAnt = Col("CodEquipamentoAntena"), cEqTx = Col("CodEquipamentoTransmissor"), cStatus = Col("Status.state"),
            cDLic = Col("DataLicenciamento"), cDPri = Col("DataPrimeiroLicenciamento"), cDVal = Col("DataValidade");

        foreach (var (name, c) in new[] { ("NumFistel", cFistel), ("NumEstacao", cEstacao), ("SiglaUf", cUf) })
            if (c < 0) throw new InvalidDataException($"Coluna obrigatória '{name}' ausente em {zip}");

        List<string>? rec;
        while ((rec = csv.ReadRecord()) != null)
        {
            if (rec.Count == 1 && string.IsNullOrWhiteSpace(rec[0])) continue; // linha em branco
            _read++;
            string? Get(int c) => c >= 0 && c < rec.Count ? Clean.Text(rec[c]) : null;

            string? fistel = Get(cFistel);
            string? numEst = Get(cEstacao);
            string? uf = Clean.Upper(Get(cUf));
            if (fistel == null || numEst == null || uf == null) { _rejected++; continue; }

            string? numAto = Get(cAto);
            string? tecnologia = Clean.Upper(Get(cTec));
            string? designacao = Clean.Upper(Get(cDesig));
            double? freqTx = Clean.Number(Get(cTx));
            double? freqRx = Clean.Number(Get(cRx));
            double? azimute = Clean.Number(Get(cAz));

            string emKey = Key(uf, fistel, numEst, numAto, Num(azimute), Num(freqTx), Num(freqRx), designacao, tecnologia);
            if (!_emissions.Add(emKey)) { _dups++; continue; }

            string stKey = Key(uf, fistel, numEst);
            if (_stations.Add(stKey))
            {
                string? entidade = Clean.Normalize(Get(cEnt));
                double? lat = Clean.Number(Get(cLat));
                double? lon = Clean.Number(Get(cLon));
                var p = insSt.Parameters;
                Set(p, "@fistel", fistel);
                Set(p, "@num_estacao", numEst);
                Set(p, "@uf", uf);
                Set(p, "@cod_municipio", Get(cCodMun));
                Set(p, "@municipio", Get(cMun));
                Set(p, "@endereco", Clean.Normalize(Get(cEnd)));
                Set(p, "@latitude", lat);
                Set(p, "@longitude", lon);
                Set(p, "@coordenada_valida", Derive.CoordenadaValida(lat, lon));
                Set(p, "@entidade", entidade);
                Set(p, "@operadora", Derive.Operadora(entidade));
                insSt.ExecuteNonQuery();
            }

            double? potW = Clean.Number(Get(cPot));
            double? elev = Clean.Number(Get(cElev));
            DateTime? dLic = Clean.Date(Get(cDLic));
            DateTime? dPri = Clean.Date(Get(cDPri));
            DateTime? dVal = Clean.Date(Get(cDVal));

            var e = insEm.Parameters;
            Set(e, "@uf", uf);
            Set(e, "@fistel", fistel);
            Set(e, "@num_estacao", numEst);
            Set(e, "@num_ato", numAto);
            Set(e, "@tecnologia", tecnologia);
            Set(e, "@geracao", Derive.Geracao(tecnologia));
            Set(e, "@designacao_emissao", designacao);
            Set(e, "@largura_banda_khz", Derive.LarguraBandaKhz(designacao));
            Set(e, "@freq_tx_mhz", freqTx);
            Set(e, "@freq_rx_mhz", freqRx);
            Set(e, "@duplex_mhz", Derive.Duplex(freqTx, freqRx));
            Set(e, "@azimute", azimute);
            Set(e, "@potencia_w", potW);
            Set(e, "@ganho_antena", Clean.Number(Get(cGanho)));
            Set(e, "@altura_antena_m", Clean.Number(Get(cAlt)));
            Set(e, "@data_licenciamento", Fmt(dLic));
            Set(e, "@data_primeiro_licenciamento", Fmt(dPri));
            Set(e, "@data_validade", Fmt(dVal));
            Set(e, "@vencida", dVal.HasValue && dVal.Value < Program.TodayRef);
            Set(e, "@anos_licenciada", Derive.AnosLicenciada(dPri));
            Set(e, "@status", Get(cStatus));
            Set(e, "@classe_estacao", Get(cClasse));
            Set(e, "@polarizacao", Clean.Upper(Get(cPol)));
            Set(e, "@angulo_elevacao_graus", elev);
            Set(e, "@angulo_meia_potencia_graus", Clean.Number(Get(cMeia)));
            Set(e, "@frente_costa_db", Clean.Number(Get(cFc)));
            Set(e, "@cod_tipo_antena", Get(cTipoAnt));
            Set(e, "@cod_equipamento_antena", Get(cEqAnt));
            Set(e, "@cod_equipamento_transmissor", Get(cEqTx));
            Set(e, "@potencia_dbm", Derive.PotenciaDbm(potW));
            Set(e, "@azimute_valido", azimute.HasValue ? azimute.Value >= 0 && azimute.Value <= 360 : (bool?)null);
            Set(e, "@elevacao_valida", elev.HasValue ? elev.Value >= -90 && elev.Value <= 90 : (bool?)null);
            insEm.ExecuteNonQuery();
            _loaded++;
        }
    }

    private static string? Num(double? d) => d?.ToString("R", CultureInfo.InvariantCulture);

    private static string? Fmt(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Key(params string?[] parts)
    {
        var sb = new StringBuilder(128);
        foreach (var p in parts)
        {
            if (p == null) sb.Append('\u0002'); else sb.Append(p);
            sb.Append('\u0001');
        }
        return sb.ToString();
    }

    private static void Set(SqliteParameterCollection p, string name, object? value)
    {
        p[name].Value = value switch
        {
            null => DBNull.Value,
            bool b => b ? 1L : 0L,
            _ => value
        };
    }
}

/// <summary>Leitor de CSV com separador '|' e campos entre aspas (aspas internas duplicadas).</summary>
internal sealed class PipeCsvReader
{
    private readonly TextReader _r;
    private readonly StringBuilder _sb = new(256);

    public PipeCsvReader(TextReader r) => _r = r;

    public List<string>? ReadRecord()
    {
        int ch = _r.Read();
        if (ch == -1) return null;
        var fields = new List<string>(48);
        _sb.Clear();
        bool inQuotes = false;
        bool fieldStart = true;
        while (true)
        {
            if (ch == -1)
            {
                fields.Add(_sb.ToString());
                return fields;
            }
            char c = (char)ch;
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (_r.Peek() == '"') { _r.Read(); _sb.Append('"'); }
                    else inQuotes = false;
                }
                else _sb.Append(c);
            }
            else if (c == '"' && fieldStart)
            {
                inQuotes = true;
                fieldStart = false;
            }
            else if (c == '|')
            {
                fields.Add(_sb.ToString());
                _sb.Clear();
                fieldStart = true;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && _r.Peek() == '\n') _r.Read();
                fields.Add(_sb.ToString());
                return fields;
            }
            else
            {
                _sb.Append(c);
                fieldStart = false;
            }
            ch = _r.Read();
        }
    }
}

internal static class Clean
{
    private static readonly Regex NumberRx = new(@"^[+-]?(\d+([.,]\d*)?|[.,]\d+)([eE][+-]?\d+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Text(string? v)
    {
        if (v == null) return null;
        var t = v.Trim();
        return t.Length == 0 ? null : t;
    }

    public static string? Upper(string? v) => v?.ToUpperInvariant();

    public static double? Number(string? v)
    {
        if (v == null || !NumberRx.IsMatch(v)) return null;
        if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return null;
        return double.IsFinite(d) ? d : null;
    }

    public static DateTime? Date(string? v)
    {
        if (v == null || v.Length != 10) return null;
        return DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    public static string? Normalize(string? v)
    {
        if (v == null) return null;
        var decomposed = v.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch);
        }
        var upper = sb.ToString().ToUpperInvariant();
        sb.Clear();
        bool pendingSpace = false;
        foreach (var ch in upper)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(ch);
            }
            else pendingSpace = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}

internal static class Derive
{
    private static readonly Regex Vivo = Word("TELEFONICA|VIVO");
    private static readonly Regex Claro = Word("CLARO|EMBRATEL");
    private static readonly Regex TimStart = new(@"^TIM\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OiStart = new(@"^OI\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Telemar = Word("TELEMAR");
    private static readonly Regex Algar = Word("ALGAR");
    private static readonly Regex Sercomtel = Word("SERCOMTEL");
    private static readonly Regex Brisanet = Word("BRISANET");

    private static Regex Word(string alt) =>
        new($@"\b(?:{alt})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Operadora(string? ent)
    {
        if (ent == null) return "OUTRAS";
        if (Vivo.IsMatch(ent)) return "VIVO";
        if (Claro.IsMatch(ent)) return "CLARO";
        if (TimStart.IsMatch(ent)) return "TIM";
        if (OiStart.IsMatch(ent) || Telemar.IsMatch(ent) || ent.Contains("BRASIL TELECOM", StringComparison.Ordinal)) return "OI";
        if (Algar.IsMatch(ent)) return "ALGAR";
        if (Sercomtel.IsMatch(ent)) return "SERCOMTEL";
        if (Brisanet.IsMatch(ent)) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tec)
    {
        if (tec == null) return "N/D";
        return tec.ToUpperInvariant() switch
        {
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA"
        };
    }

    public static double? LarguraBandaKhz(string? desig)
    {
        if (desig == null || desig.Length < 4) return null;
        var s = desig.Substring(0, 4).ToUpperInvariant();
        int unitPos = -1;
        decimal factor = 0;
        for (int i = 0; i < 4; i++)
        {
            char c = s[i];
            if (c >= '0' && c <= '9') continue;
            decimal f = c switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, 'G' => 1000000m, _ => -1m };
            if (f < 0 || unitPos >= 0 || i == 0) return null;
            unitPos = i;
            factor = f;
        }
        if (unitPos < 0) return null;
        string intPart = s.Substring(0, unitPos);
        string frac = s.Substring(unitPos + 1);
        decimal val = decimal.Parse(frac.Length > 0 ? intPart + "." + frac : intPart, CultureInfo.InvariantCulture);
        return (double)(val * factor);
    }

    public static double? Duplex(double? tx, double? rx)
    {
        if (tx == null || rx == null) return null;
        try
        {
            return (double)Math.Round((decimal)tx.Value - (decimal)rx.Value, 3, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return Math.Round(tx.Value - rx.Value, 3, MidpointRounding.AwayFromZero);
        }
    }

    public static double? PotenciaDbm(double? w)
    {
        if (w == null || w.Value <= 0) return null;
        return Round(10.0 * Math.Log10(w.Value * 1000.0), 1);
    }

    public static double? AnosLicenciada(DateTime? primeiro)
    {
        if (primeiro == null) return null;
        decimal days = (decimal)(Program.TodayRef - primeiro.Value).TotalDays;
        return (double)Math.Round(days / 365.25m, 1, MidpointRounding.AwayFromZero);
    }

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (lat == null || lon == null) return false;
        double la = lat.Value, lo = lon.Value;
        if (la == 0 && lo == 0) return false;
        return la >= -33.75 && la <= 5.28 && lo >= -73.99 && lo <= -28.83;
    }

    private static double Round(double v, int digits)
    {
        if (!double.IsFinite(v)) return v;
        try { return (double)Math.Round((decimal)v, digits, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return Math.Round(v, digits, MidpointRounding.AwayFromZero); }
    }
}
