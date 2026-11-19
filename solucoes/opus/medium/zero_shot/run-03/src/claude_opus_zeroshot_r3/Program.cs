using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Etl;

public static class Program
{
    const string DbName = "claude_opus_zeroshot_r3.db";

    public static int Main()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            string root = ResolveRoot();
            string filesDir = Path.Combine(root, "files");
            string ddlPath = Path.Combine(root, "src", "DDL.sql");
            string dbDir = Path.Combine(root, "db");
            Directory.CreateDirectory(dbDir);
            string dbPath = Path.Combine(dbDir, DbName);

            foreach (var f in new[] { dbPath, dbPath + "-journal", dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);

            var zips = Directory.GetFiles(filesDir, "*.zip")
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (zips.Count == 0) throw new InvalidOperationException($"Nenhum ZIP encontrado em {filesDir}");

            using var loader = new Loader(dbPath, File.ReadAllText(ddlPath, Encoding.UTF8));
            foreach (var zip in zips)
            {
                var st = loader.ProcessZip(zip);
                Console.WriteLine($"{Path.GetFileName(zip)}: lidas={st.Read} rejeitadas={st.Rejected} " +
                                  $"duplicadas={st.Duplicates} emissoes={st.Emissions} estacoes={st.Stations}");
            }
            loader.Commit();
            Console.WriteLine($"Concluído em {sw.Elapsed}. Banco: {dbPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
    }

    static string ResolveRoot()
    {
        const string winRoot = @"D:\Projects\TCC";
        if (OperatingSystem.IsWindows() && IsRoot(winRoot)) return winRoot;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                if (IsRoot(d.FullName)) return d.FullName;
                d = d.Parent;
            }
        }
        if (IsRoot(winRoot)) return winRoot;
        throw new DirectoryNotFoundException("Não foi possível localizar a pasta raiz (com 'files' e 'src/DDL.sql').");
    }

    static bool IsRoot(string p) =>
        Directory.Exists(Path.Combine(p, "files")) && File.Exists(Path.Combine(p, "src", "DDL.sql"));
}

sealed record FileStats(int Read, int Rejected, int Duplicates, int Emissions, int Stations);

static class Clean
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    public static string? Text(string? v)
    {
        if (v == null) return null;
        v = v.Trim();
        return v.Length == 0 ? null : v;
    }

    public static string? Upper(string? v) => Text(v)?.ToUpperInvariant();

    public static double? Number(string? v)
    {
        v = Text(v);
        if (v == null) return null;
        v = v.Replace(',', '.');
        if (!double.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var d))
            return null;
        return double.IsFinite(d) ? d : null;
    }

    public static DateOnly? Date(string? v)
    {
        v = Text(v);
        if (v == null || v.Length != 10) return null;
        return DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    public static string? Normalize(string? v)
    {
        v = Text(v);
        if (v == null) return null;
        var decomposed = v.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        var upper = sb.ToString().ToUpperInvariant();
        sb.Clear();
        bool pendingSpace = false;
        foreach (var c in upper)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
            }
            else pendingSpace = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    public static double Round(double x, int digits) =>
        (double)Math.Round((decimal)x, digits, MidpointRounding.AwayFromZero);
}

static class Derive
{
    static readonly Regex RxVivo = new(@"\b(TELEFONICA|VIVO)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex RxClaro = new(@"\b(CLARO|EMBRATEL)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex RxTim = new(@"^TIM\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex RxOi = new(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex RxAlgar = new(@"\bALGAR\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex RxSercomtel = new(@"\bSERCOMTEL\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex RxBrisanet = new(@"\bBRISANET\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Operadora(string? entidade)
    {
        if (entidade == null) return "OUTRAS";
        if (RxVivo.IsMatch(entidade)) return "VIVO";
        if (RxClaro.IsMatch(entidade)) return "CLARO";
        if (RxTim.IsMatch(entidade)) return "TIM";
        if (RxOi.IsMatch(entidade)) return "OI";
        if (RxAlgar.IsMatch(entidade)) return "ALGAR";
        if (RxSercomtel.IsMatch(entidade)) return "SERCOMTEL";
        if (RxBrisanet.IsMatch(entidade)) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tecnologia)
    {
        if (tecnologia == null) return "N/D";
        return tecnologia.ToUpperInvariant() switch
        {
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA",
        };
    }

    public static double? LarguraBandaKhz(string? designacao)
    {
        if (designacao == null || designacao.Length < 4) return null;
        var s = designacao.Substring(0, 4).ToUpperInvariant();
        int unitPos = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = s[i];
            if (c is 'H' or 'K' or 'M' or 'G')
            {
                if (i == 0 || unitPos >= 0) return null;
                unitPos = i;
            }
            else if (c < '0' || c > '9') return null;
        }
        if (unitPos < 0) return null;
        decimal factor = s[unitPos] switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, _ => 1_000_000m };
        string intPart = s.Substring(0, unitPos);
        string fracPart = s.Substring(unitPos + 1);
        var num = decimal.Parse(fracPart.Length > 0 ? intPart + "." + fracPart : intPart, CultureInfo.InvariantCulture);
        return (double)(num * factor);
    }

    public static bool CoordenadaValida(double? lat, double? lon)
    {
        if (lat is not double la || lon is not double lo) return false;
        if (la == 0 && lo == 0) return false;
        return la >= -33.75 && la <= 5.28 && lo >= -73.99 && lo <= -28.83;
    }
}

/// <summary>Leitor CSV em streaming: separador '|', aspas duplas, aspas escapadas por duplicação.</summary>
sealed class CsvReader
{
    readonly TextReader _r;
    readonly char _sep;
    readonly StringBuilder _field = new();

    public CsvReader(TextReader r, char sep) { _r = r; _sep = sep; }

    public List<string>? ReadRecord()
    {
        int c = _r.Read();
        while (c == '\r' || c == '\n') c = _r.Read(); // ignora linhas vazias
        if (c == -1) return null;
        var rec = new List<string>(48);
        _field.Clear();
        bool inQuotes = false;
        while (true)
        {
            if (inQuotes)
            {
                if (c == -1) { rec.Add(_field.ToString()); return rec; }
                if (c == '"')
                {
                    int n = _r.Peek();
                    if (n == '"') { _r.Read(); _field.Append('"'); }
                    else inQuotes = false;
                }
                else _field.Append((char)c);
            }
            else
            {
                if (c == -1 || c == '\n' || c == '\r')
                {
                    if (c == '\r' && _r.Peek() == '\n') _r.Read();
                    rec.Add(_field.ToString());
                    return rec;
                }
                if (c == _sep) { rec.Add(_field.ToString()); _field.Clear(); }
                else if (c == '"') inQuotes = true;
                else _field.Append((char)c);
            }
            c = _r.Read();
        }
    }
}

sealed class Station
{
    public required string Uf, Fistel, NumEstacao;
    public string? CodMunicipio, Municipio, Endereco, Entidade;
    public double? Latitude, Longitude;
    public bool CoordenadaValida;
    public required string Operadora;
}

sealed class Loader : IDisposable
{
    static readonly string[] EmissaoCols =
    {
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao", "largura_banda_khz",
        "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w", "ganho_antena", "altura_antena_m",
        "data_licenciamento", "data_primeiro_licenciamento", "data_validade", "vencida", "anos_licenciada",
        "status", "classe_estacao", "polarizacao", "angulo_elevacao_graus", "angulo_meia_potencia_graus",
        "frente_costa_db", "cod_tipo_antena", "cod_equipamento_antena", "cod_equipamento_transmissor",
        "potencia_dbm", "azimute_valido", "elevacao_valida",
    };

    static readonly string[] EstacaoCols =
    {
        "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude",
        "coordenada_valida", "entidade", "operadora",
    };

    static readonly string[] RequiredHeaders =
    {
        "NumFistel", "NumEstacao", "NumAto", "SiglaUf", "CodMunicipio", "Municipio.NomeMunicipio", "NomeEntidade",
        "EnderecoEstacao", "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao", "FreqTxMHz", "FreqRxMHz",
        "Azimute", "PotenciaTransmissorWatts", "GanhoAntena", "AlturaAntena", "AnguloElevacao",
        "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao", "CodTipoClasseEstacao", "CodTipoAntena",
        "CodEquipamentoAntena", "CodEquipamentoTransmissor", "Status.state", "DataLicenciamento",
        "DataPrimeiroLicenciamento", "DataValidade",
    };

    readonly HashSet<(string, string, string)> _stations = new();
    readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    readonly SqliteConnection _conn;
    SqliteTransaction _tx;
    readonly SqliteCommand _insEmissao, _insEstacao;
    readonly SqliteParameter[] _pEmi, _pEst;

    public Loader(string dbPath, string ddl)
    {
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _conn.Open();
        Exec("PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-200000;");
        Exec(ddl);
        _tx = _conn.BeginTransaction();
        (_insEmissao, _pEmi) = Prepare("emissao", EmissaoCols);
        (_insEstacao, _pEst) = Prepare("estacao", EstacaoCols);
    }

    void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    (SqliteCommand, SqliteParameter[]) Prepare(string table, string[] cols)
    {
        var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select((_, i) => "$p" + i))})";
        var ps = new SqliteParameter[cols.Length];
        for (int i = 0; i < cols.Length; i++) ps[i] = cmd.Parameters.Add(new SqliteParameter("$p" + i, null));
        cmd.Prepare();
        return (cmd, ps);
    }

    static object Db(string? s) => (object?)s ?? DBNull.Value;
    static object Db(double? d) => d.HasValue ? d.Value : DBNull.Value;
    static object Db(bool? b) => b.HasValue ? (b.Value ? 1L : 0L) : DBNull.Value;
    static object Db(DateOnly? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;

    static string K(string? s) => s == null ? "\u0001" : s.Replace("\u0002", "");
    static string K(double? d) => d.HasValue ? d.Value.ToString("R", CultureInfo.InvariantCulture) : "\u0001";

    public FileStats ProcessZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e => e.Length > 0)
                    ?? throw new InvalidDataException($"ZIP sem CSV: {zipPath}");
        using var stream = entry.Open();
        using var sr = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        var csv = new CsvReader(sr, '|');

        var header = csv.ReadRecord() ?? throw new InvalidDataException($"CSV vazio: {zipPath}");
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            var h = header[i].Trim().TrimStart('\uFEFF');
            idx.TryAdd(h, i);
        }
        var ci = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in idx) ci.TryAdd(kv.Key, kv.Value);
        int Col(string name) => idx.TryGetValue(name, out var i) ? i : ci.TryGetValue(name, out i) ? i : -1;
        var missing = RequiredHeaders.Where(h => Col(h) < 0).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"{zipPath}: colunas ausentes no cabeçalho: {string.Join(", ", missing)}");

        int cFistel = Col("NumFistel"), cEstacao = Col("NumEstacao"), cAto = Col("NumAto"), cUf = Col("SiglaUf"),
            cCodMun = Col("CodMunicipio"), cMun = Col("Municipio.NomeMunicipio"), cEnt = Col("NomeEntidade"),
            cEnd = Col("EnderecoEstacao"), cLat = Col("Latitude"), cLon = Col("Longitude"), cTec = Col("Tecnologia"),
            cDes = Col("DesignacaoEmissao"), cTx = Col("FreqTxMHz"), cRx = Col("FreqRxMHz"), cAz = Col("Azimute"),
            cPot = Col("PotenciaTransmissorWatts"), cGanho = Col("GanhoAntena"), cAlt = Col("AlturaAntena"),
            cElev = Col("AnguloElevacao"), cMeia = Col("AnguloMeiaPotenciaAntena"), cFc = Col("FrenteCostaAntena"),
            cPol = Col("Polarizacao"), cClasse = Col("CodTipoClasseEstacao"), cTipoAnt = Col("CodTipoAntena"),
            cEqAnt = Col("CodEquipamentoAntena"), cEqTx = Col("CodEquipamentoTransmissor"), cStatus = Col("Status.state"),
            cDLic = Col("DataLicenciamento"), cDPri = Col("DataPrimeiroLicenciamento"), cDVal = Col("DataValidade");

        var stations = _stations;
        var stationOrder = new List<Station>();
        var seen = _seen;
        int read = 0, rejected = 0, dups = 0, emissions = 0;
        var kb = new StringBuilder(256);

        List<string>? rec;
        while ((rec = csv.ReadRecord()) != null)
        {
            read++;
            string? F(int i) => i < rec.Count ? rec[i] : null;

            string? fistel = Clean.Text(F(cFistel));
            string? numEst = Clean.Text(F(cEstacao));
            string? uf = Clean.Upper(F(cUf));
            if (fistel == null || numEst == null || uf == null) { rejected++; continue; }

            string? numAto = Clean.Text(F(cAto));
            string? tecnologia = Clean.Upper(F(cTec));
            string? designacao = Clean.Upper(F(cDes));
            double? tx = Clean.Number(F(cTx));
            double? rx = Clean.Number(F(cRx));
            double? az = Clean.Number(F(cAz));

            // Estação (primeira ocorrência)
            var skey = (uf, fistel, numEst);
            if (stations.Add(skey))
            {
                double? lat = Clean.Number(F(cLat)), lon = Clean.Number(F(cLon));
                string? entidade = Clean.Normalize(F(cEnt));
                var s = new Station
                {
                    Uf = uf, Fistel = fistel, NumEstacao = numEst,
                    CodMunicipio = Clean.Text(F(cCodMun)),
                    Municipio = Clean.Text(F(cMun)),
                    Endereco = Clean.Normalize(F(cEnd)),
                    Latitude = lat, Longitude = lon,
                    CoordenadaValida = Derive.CoordenadaValida(lat, lon),
                    Entidade = entidade,
                    Operadora = Derive.Operadora(entidade),
                };
                stationOrder.Add(s);
                InsertStation(s);
            }

            // Deduplicação de emissão
            kb.Clear();
            kb.Append(K(uf)).Append('\u0002').Append(K(fistel)).Append('\u0002').Append(K(numEst)).Append('\u0002')
              .Append(K(numAto)).Append('\u0002').Append(K(az)).Append('\u0002').Append(K(tx)).Append('\u0002')
              .Append(K(rx)).Append('\u0002').Append(K(designacao)).Append('\u0002').Append(K(tecnologia));
            if (!seen.Add(kb.ToString())) { dups++; continue; }

            double? pot = Clean.Number(F(cPot));
            double? elev = Clean.Number(F(cElev));
            DateOnly? dLic = Clean.Date(F(cDLic)), dPri = Clean.Date(F(cDPri)), dVal = Clean.Date(F(cDVal));

            double? duplex = tx.HasValue && rx.HasValue ? Clean.Round(tx.Value - rx.Value, 3) : null;
            double? dbm = pot is double w && w > 0 ? Clean.Round(10.0 * Math.Log10(w * 1000.0), 1) : null;
            bool vencida = dVal.HasValue && dVal.Value < Clean.Today;
            double? anos = dPri.HasValue ? Clean.Round((Clean.Today.DayNumber - dPri.Value.DayNumber) / 365.25, 1) : null;
            bool? azOk = az.HasValue ? az.Value >= 0 && az.Value <= 360 : null;
            bool? elevOk = elev.HasValue ? elev.Value >= -90 && elev.Value <= 90 : null;

            var p = _pEmi;
            p[0].Value = uf;
            p[1].Value = fistel;
            p[2].Value = numEst;
            p[3].Value = Db(numAto);
            p[4].Value = Db(tecnologia);
            p[5].Value = Derive.Geracao(tecnologia);
            p[6].Value = Db(designacao);
            p[7].Value = Db(Derive.LarguraBandaKhz(designacao));
            p[8].Value = Db(tx);
            p[9].Value = Db(rx);
            p[10].Value = Db(duplex);
            p[11].Value = Db(az);
            p[12].Value = Db(pot);
            p[13].Value = Db(Clean.Number(F(cGanho)));
            p[14].Value = Db(Clean.Number(F(cAlt)));
            p[15].Value = Db(dLic);
            p[16].Value = Db(dPri);
            p[17].Value = Db(dVal);
            p[18].Value = vencida ? 1L : 0L;
            p[19].Value = Db(anos);
            p[20].Value = Db(Clean.Text(F(cStatus)));
            p[21].Value = Db(Clean.Text(F(cClasse)));
            p[22].Value = Db(Clean.Upper(F(cPol)));
            p[23].Value = Db(elev);
            p[24].Value = Db(Clean.Number(F(cMeia)));
            p[25].Value = Db(Clean.Number(F(cFc)));
            p[26].Value = Db(Clean.Text(F(cTipoAnt)));
            p[27].Value = Db(Clean.Text(F(cEqAnt)));
            p[28].Value = Db(Clean.Text(F(cEqTx)));
            p[29].Value = Db(dbm);
            p[30].Value = Db(azOk);
            p[31].Value = Db(elevOk);
            _insEmissao.ExecuteNonQuery();
            emissions++;
        }

        return new FileStats(read, rejected, dups, emissions, stationOrder.Count);
    }

    void InsertStation(Station s)
    {
        var q = _pEst;
        q[0].Value = s.Fistel;
        q[1].Value = s.NumEstacao;
        q[2].Value = s.Uf;
        q[3].Value = Db(s.CodMunicipio);
        q[4].Value = Db(s.Municipio);
        q[5].Value = Db(s.Endereco);
        q[6].Value = Db(s.Latitude);
        q[7].Value = Db(s.Longitude);
        q[8].Value = s.CoordenadaValida ? 1L : 0L;
        q[9].Value = Db(s.Entidade);
        q[10].Value = s.Operadora;
        _insEstacao.ExecuteNonQuery();
    }

    public void Commit()
    {
        _tx.Commit();
    }

    public void Dispose()
    {
        _insEmissao.Dispose();
        _insEstacao.Dispose();
        _tx.Dispose();
        _conn.Dispose();
    }
}
