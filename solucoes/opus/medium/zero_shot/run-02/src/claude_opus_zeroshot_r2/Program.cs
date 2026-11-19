using System.Globalization;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Etl;

public static class Program
{
    const string DbName = "claude_opus_zeroshot_r2.db";

    public static int Main()
    {
        try
        {
            var root = FindRoot();
            var filesDir = Path.Combine(root, "files");
            var ddlPath = Path.Combine(root, "src", "DDL.sql");
            var dbDir = Path.Combine(root, "db");
            Directory.CreateDirectory(dbDir);
            var dbPath = Path.Combine(dbDir, DbName);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            new Pipeline(filesDir, ddlPath, dbPath).Run();
            Console.WriteLine($"Concluído em {sw.Elapsed}. Banco: {dbPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
    }

    static string FindRoot()
    {
        const string fixedRoot = @"D:\Projects\TCC";
        if (IsRoot(fixedRoot)) return fixedRoot;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                if (IsRoot(d.FullName)) return d.FullName;
                d = d.Parent;
            }
        }
        throw new DirectoryNotFoundException("Pasta raiz (com 'files' e 'src/DDL.sql') não encontrada.");
    }

    static bool IsRoot(string p) =>
        Directory.Exists(Path.Combine(p, "files")) && File.Exists(Path.Combine(p, "src", "DDL.sql"));
}

sealed class Pipeline(string filesDir, string ddlPath, string dbPath)
{

    static readonly string[] Ufs =
    {
        "AC","AL","AM","AP","BA","CE","DF","ES","GO","MA","MG","MS","MT","PA",
        "PB","PE","PI","PR","RJ","RN","RO","RR","RS","SC","SE","SP","TO"
    };

    readonly HashSet<UInt128> _stations = new();
    readonly HashSet<UInt128> _emissions = new();

    long _read, _rejected, _dupes, _stationCount, _emissionCount;

    public void Run()
    {
        if (File.Exists(dbPath)) File.Delete(dbPath);

        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        conn.Open();

        Exec(conn, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-200000;");
        Exec(conn, File.ReadAllText(ddlPath, Encoding.UTF8));

        var zips = Ufs.Select(u => Path.Combine(filesDir, u + ".zip")).ToList();
        var missing = zips.Where(z => !File.Exists(z)).ToList();
        if (missing.Count > 0)
            throw new FileNotFoundException("Arquivos ZIP ausentes: " + string.Join(", ", missing));

        using var writer = new Writer(conn);
        foreach (var zip in zips)
        {
            var before = _read;
            ProcessZip(zip, writer);
            Console.WriteLine($"{Path.GetFileName(zip)}: {_read - before} linhas lidas");
        }
        writer.Commit();

        Console.WriteLine($"Lidas={_read} Rejeitadas={_rejected} Duplicadas={_dupes} Estações={_stationCount} Emissões={_emissionCount}");
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    void ProcessZip(string zipPath, Writer writer)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e => e.Length > 0)
                    ?? throw new InvalidDataException($"{zipPath}: nenhum CSV encontrado.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);
        var csv = new CsvReader(reader);

        var header = csv.ReadRecord() ?? throw new InvalidDataException($"{zipPath}: arquivo vazio.");
        var cols = new Columns(header, zipPath);

        List<string>? rec;
        while ((rec = csv.ReadRecord()) != null)
        {
            if (rec.Count == 1 && string.IsNullOrWhiteSpace(rec[0])) continue; // linha em branco
            _read++;
            ProcessRow(rec, cols, writer);
        }
    }

    void ProcessRow(List<string> r, Columns c, Writer w)
    {
        var fistel = Clean.Text(c.Get(r, c.NumFistel));
        var numEst = Clean.Text(c.Get(r, c.NumEstacao));
        var uf = Clean.Upper(c.Get(r, c.SiglaUf));
        if (fistel == null || numEst == null || uf == null) { _rejected++; return; }

        var e = new Emission
        {
            Uf = uf,
            Fistel = fistel,
            NumEstacao = numEst,
            NumAto = Clean.Text(c.Get(r, c.NumAto)),
            Tecnologia = Clean.Upper(c.Get(r, c.Tecnologia)),
            Designacao = Clean.Upper(c.Get(r, c.DesignacaoEmissao)),
            FreqTx = Clean.Number(c.Get(r, c.FreqTx)),
            FreqRx = Clean.Number(c.Get(r, c.FreqRx)),
            Azimute = Clean.Number(c.Get(r, c.Azimute)),
            PotenciaW = Clean.Number(c.Get(r, c.Potencia)),
            Ganho = Clean.Number(c.Get(r, c.Ganho)),
            Altura = Clean.Number(c.Get(r, c.Altura)),
            DataLic = Clean.Date(c.Get(r, c.DataLic)),
            DataPrimLic = Clean.Date(c.Get(r, c.DataPrimLic)),
            DataValidade = Clean.Date(c.Get(r, c.DataValidade)),
            Status = Clean.Text(c.Get(r, c.Status)),
            Classe = Clean.Text(c.Get(r, c.Classe)),
            Polarizacao = Clean.Upper(c.Get(r, c.Polarizacao)),
            Elevacao = Clean.Number(c.Get(r, c.Elevacao)),
            MeiaPotencia = Clean.Number(c.Get(r, c.MeiaPotencia)),
            FrenteCosta = Clean.Number(c.Get(r, c.FrenteCosta)),
            CodTipoAntena = Clean.Text(c.Get(r, c.CodTipoAntena)),
            CodEquipAntena = Clean.Text(c.Get(r, c.CodEquipAntena)),
            CodEquipTx = Clean.Text(c.Get(r, c.CodEquipTx)),
        };

        // Estação (primeira ocorrência)
        var stKey = Hash(uf, fistel, numEst);
        if (_stations.Add(stKey))
        {
            var entidade = Clean.Normalize(c.Get(r, c.NomeEntidade));
            var lat = Clean.Number(c.Get(r, c.Latitude));
            var lon = Clean.Number(c.Get(r, c.Longitude));
            w.InsertStation(new Station
            {
                Uf = uf,
                Fistel = fistel,
                NumEstacao = numEst,
                CodMunicipio = Clean.Text(c.Get(r, c.CodMunicipio)),
                Municipio = Clean.Text(c.Get(r, c.Municipio)),
                Endereco = Clean.Normalize(c.Get(r, c.Endereco)),
                Latitude = lat,
                Longitude = lon,
                CoordenadaValida = Derive.CoordValid(lat, lon),
                Entidade = entidade,
                Operadora = Derive.Operadora(entidade),
            });
            _stationCount++;
        }

        // Deduplicação de emissões
        var emKey = Hash(uf, fistel, numEst, e.NumAto, Num(e.Azimute), Num(e.FreqTx), Num(e.FreqRx), e.Designacao, e.Tecnologia);
        if (!_emissions.Add(emKey)) { _dupes++; return; }

        w.InsertEmission(e);
        _emissionCount++;
    }

    static string? Num(double? d) => d?.ToString("R", CultureInfo.InvariantCulture);

    static UInt128 Hash(params string?[] parts)
    {
        var sb = new StringBuilder(128);
        foreach (var p in parts)
        {
            if (p == null) sb.Append('\u0001');
            else sb.Append('\u0002').Append(p);
            sb.Append('\u0000');
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return XxHash128.HashToUInt128(bytes);
    }
}

sealed class Columns
{
    readonly Dictionary<string, int> _map;
    public readonly int NumFistel, NumEstacao, NumAto, SiglaUf, CodMunicipio, Municipio, NomeEntidade, Endereco,
        Latitude, Longitude, Tecnologia, DesignacaoEmissao, FreqTx, FreqRx, Azimute, Potencia, Ganho, Altura,
        Elevacao, MeiaPotencia, FrenteCosta, Polarizacao, Classe, CodTipoAntena, CodEquipAntena, CodEquipTx,
        Status, DataLic, DataPrimLic, DataValidade;

    public Columns(List<string> header, string file)
    {
        _map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim().TrimStart('\uFEFF');
            _map.TryAdd(name, i);
        }
        int Req(string n) => _map.TryGetValue(n, out var i) ? i
            : throw new InvalidDataException($"{file}: coluna '{n}' não encontrada no cabeçalho.");

        NumFistel = Req("NumFistel");
        NumEstacao = Req("NumEstacao");
        NumAto = Req("NumAto");
        SiglaUf = Req("SiglaUf");
        CodMunicipio = Req("CodMunicipio");
        Municipio = Req("Municipio.NomeMunicipio");
        NomeEntidade = Req("NomeEntidade");
        Endereco = Req("EnderecoEstacao");
        Latitude = Req("Latitude");
        Longitude = Req("Longitude");
        Tecnologia = Req("Tecnologia");
        DesignacaoEmissao = Req("DesignacaoEmissao");
        FreqTx = Req("FreqTxMHz");
        FreqRx = Req("FreqRxMHz");
        Azimute = Req("Azimute");
        Potencia = Req("PotenciaTransmissorWatts");
        Ganho = Req("GanhoAntena");
        Altura = Req("AlturaAntena");
        Elevacao = Req("AnguloElevacao");
        MeiaPotencia = Req("AnguloMeiaPotenciaAntena");
        FrenteCosta = Req("FrenteCostaAntena");
        Polarizacao = Req("Polarizacao");
        Classe = Req("CodTipoClasseEstacao");
        CodTipoAntena = Req("CodTipoAntena");
        CodEquipAntena = Req("CodEquipamentoAntena");
        CodEquipTx = Req("CodEquipamentoTransmissor");
        Status = Req("Status.state");
        DataLic = Req("DataLicenciamento");
        DataPrimLic = Req("DataPrimeiroLicenciamento");
        DataValidade = Req("DataValidade");
    }

    public string? Get(List<string> r, int i) => i < r.Count ? r[i] : null;
}

/// <summary>Leitor de CSV com separador '|' e aspas duplas (RFC 4180-like), aceitando quebras de linha dentro de aspas.</summary>
sealed class CsvReader(TextReader reader)
{
    readonly StringBuilder _field = new(256);

    public List<string>? ReadRecord()
    {
        int ch = reader.Read();
        if (ch == -1) return null;

        var rec = new List<string>(48);
        _field.Clear();
        bool inQuotes = false, wasQuoted = false;

        while (true)
        {
            if (inQuotes)
            {
                if (ch == -1) { rec.Add(_field.ToString()); return rec; }
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); _field.Append('"'); }
                    else inQuotes = false;
                }
                else _field.Append((char)ch);
            }
            else
            {
                switch (ch)
                {
                    case -1:
                        rec.Add(_field.ToString());
                        return rec;
                    case '|':
                        rec.Add(_field.ToString());
                        _field.Clear();
                        wasQuoted = false;
                        break;
                    case '\r':
                        if (reader.Peek() == '\n') reader.Read();
                        rec.Add(_field.ToString());
                        return rec;
                    case '\n':
                        rec.Add(_field.ToString());
                        return rec;
                    case '"':
                        if (!wasQuoted && _field.Length == 0) { inQuotes = true; wasQuoted = true; }
                        else _field.Append('"');
                        break;
                    default:
                        _field.Append((char)ch);
                        break;
                }
            }
            ch = reader.Read();
        }
    }
}

static class Clean
{
    public static string? Text(string? v)
    {
        if (v == null) return null;
        v = v.Trim();
        return v.Length == 0 ? null : v;
    }

    public static string? Upper(string? v) => Text(v)?.ToUpperInvariant();

    public static double? Number(string? v)
    {
        var t = Text(v);
        if (t == null) return null;
        t = t.Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var d)) return null;
        return double.IsFinite(d) ? d : null;
    }

    public static DateTime? Date(string? v)
    {
        var t = Text(v);
        if (t == null || t.Length != 10) return null;
        return DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    public static string? Normalize(string? v)
    {
        var t = Text(v);
        if (t == null) return null;
        var decomposed = t.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool lastSpace = true;
        foreach (var ch0 in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch0);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            var ch = char.ToUpperInvariant(ch0);
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.Length == 0 ? null : sb.ToString();
    }
}

static class Derive
{
    static readonly DateTime Today = new(2026, 10, 15);

    public static double? Round(double? v, int digits)
    {
        if (v == null) return null;
        var x = v.Value;
        if (Math.Abs(x) < 7.9e27)
        {
            try { return (double)Math.Round((decimal)x, digits, MidpointRounding.AwayFromZero); }
            catch (OverflowException) { }
        }
        return Math.Round(x, digits, MidpointRounding.AwayFromZero);
    }

    public static string Operadora(string? ent)
    {
        if (ent == null) return "OUTRAS";
        var words = ent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var set = new HashSet<string>(words, StringComparer.Ordinal);
        var padded = " " + string.Join(' ', words) + " ";

        if (set.Contains("TELEFONICA") || set.Contains("VIVO")) return "VIVO";
        if (set.Contains("CLARO") || set.Contains("EMBRATEL")) return "CLARO";
        if (words.Length > 0 && words[0] == "TIM") return "TIM";
        if ((words.Length > 0 && words[0] == "OI") || set.Contains("TELEMAR") || padded.Contains(" BRASIL TELECOM ")) return "OI";
        if (set.Contains("ALGAR")) return "ALGAR";
        if (set.Contains("SERCOMTEL")) return "SERCOMTEL";
        if (set.Contains("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    public static string Geracao(string? tec)
    {
        if (string.IsNullOrWhiteSpace(tec)) return "N/D";
        return tec.Trim().ToUpperInvariant() switch
        {
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA",
        };
    }

    public static double? LarguraBanda(string? des)
    {
        if (des == null || des.Length < 4) return null;
        var s = des.Substring(0, 4).ToUpperInvariant();
        int unitPos = -1;
        decimal factor = 0;
        for (int i = 0; i < 4; i++)
        {
            char ch = s[i];
            if (ch is >= '0' and <= '9') continue;
            decimal f = ch switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, 'G' => 1000000m, _ => -1m };
            if (f < 0 || i == 0 || unitPos >= 0) return null;
            unitPos = i;
            factor = f;
        }
        if (unitPos < 0) return null;
        var intPart = s.Substring(0, unitPos);
        var fracPart = s.Substring(unitPos + 1);
        var num = decimal.Parse(fracPart.Length > 0 ? intPart + "." + fracPart : intPart, CultureInfo.InvariantCulture);
        return (double)(num * factor);
    }

    public static double? Duplex(double? tx, double? rx)
    {
        if (tx == null || rx == null) return null;
        try { return (double)Math.Round((decimal)tx.Value - (decimal)rx.Value, 3, MidpointRounding.AwayFromZero); }
        catch (OverflowException) { return Round(tx.Value - rx.Value, 3); }
    }

    public static double? PotenciaDbm(double? w)
    {
        if (w == null || w.Value <= 0) return null;
        return Round(10.0 * Math.Log10(w.Value * 1000.0), 1);
    }

    public static bool Vencida(DateTime? validade) => validade != null && validade.Value < Today;

    public static double? AnosLicenciada(DateTime? prim)
    {
        if (prim == null) return null;
        var days = (decimal)(Today - prim.Value).Days;
        return (double)Math.Round(days / 365.25m, 1, MidpointRounding.AwayFromZero);
    }

    public static bool CoordValid(double? lat, double? lon)
    {
        if (lat == null || lon == null) return false;
        double a = lat.Value, o = lon.Value;
        if (a == 0 && o == 0) return false;
        return a >= -33.75 && a <= 5.28 && o >= -73.99 && o <= -28.83;
    }

    public static bool? Range(double? v, double min, double max) => v == null ? null : v.Value >= min && v.Value <= max;
}

sealed class Station
{
    public required string Uf, Fistel, NumEstacao;
    public string? CodMunicipio, Municipio, Endereco, Entidade;
    public double? Latitude, Longitude;
    public bool CoordenadaValida;
    public required string Operadora;
}

sealed class Emission
{
    public required string Uf, Fistel, NumEstacao;
    public string? NumAto, Tecnologia, Designacao, Status, Classe, Polarizacao, CodTipoAntena, CodEquipAntena, CodEquipTx;
    public double? FreqTx, FreqRx, Azimute, PotenciaW, Ganho, Altura, Elevacao, MeiaPotencia, FrenteCosta;
    public DateTime? DataLic, DataPrimLic, DataValidade;
}

sealed class Writer : IDisposable
{
    readonly SqliteConnection _conn;
    SqliteTransaction _tx;
    readonly SqliteCommand _stCmd, _emCmd;
    readonly SqliteParameter[] _stP, _emP;
    int _pending;

    static readonly string[] StCols =
    {
        "fistel","num_estacao","uf","cod_municipio","municipio","endereco","latitude","longitude",
        "coordenada_valida","entidade","operadora"
    };

    static readonly string[] EmCols =
    {
        "uf","fistel","num_estacao","num_ato","tecnologia","geracao","designacao_emissao","largura_banda_khz",
        "freq_tx_mhz","freq_rx_mhz","duplex_mhz","azimute","potencia_w","ganho_antena","altura_antena_m",
        "data_licenciamento","data_primeiro_licenciamento","data_validade","vencida","anos_licenciada",
        "status","classe_estacao","polarizacao","angulo_elevacao_graus","angulo_meia_potencia_graus",
        "frente_costa_db","cod_tipo_antena","cod_equipamento_antena","cod_equipamento_transmissor",
        "potencia_dbm","azimute_valido","elevacao_valida"
    };

    public Writer(SqliteConnection conn)
    {
        _conn = conn;
        _tx = conn.BeginTransaction();
        (_stCmd, _stP) = Prepare("estacao", StCols);
        (_emCmd, _emP) = Prepare("emissao", EmCols);
    }

    (SqliteCommand, SqliteParameter[]) Prepare(string table, string[] cols)
    {
        var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select((_, i) => "$p" + i))})";
        var ps = cols.Select((_, i) => cmd.Parameters.Add(new SqliteParameter("$p" + i, null))).ToArray();
        cmd.Prepare();
        return (cmd, ps);
    }

    static object V(string? s) => (object?)s ?? DBNull.Value;
    static object V(double? d) => d.HasValue ? d.Value : DBNull.Value;
    static object V(bool? b) => b.HasValue ? (b.Value ? 1L : 0L) : DBNull.Value;
    static object V(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;

    public void InsertStation(Station s)
    {
        var p = _stP;
        p[0].Value = s.Fistel;
        p[1].Value = s.NumEstacao;
        p[2].Value = s.Uf;
        p[3].Value = V(s.CodMunicipio);
        p[4].Value = V(s.Municipio);
        p[5].Value = V(s.Endereco);
        p[6].Value = V(s.Latitude);
        p[7].Value = V(s.Longitude);
        p[8].Value = V(s.CoordenadaValida);
        p[9].Value = V(s.Entidade);
        p[10].Value = s.Operadora;
        _stCmd.ExecuteNonQuery();
        Tick();
    }

    public void InsertEmission(Emission e)
    {
        var p = _emP;
        int i = 0;
        p[i++].Value = e.Uf;
        p[i++].Value = e.Fistel;
        p[i++].Value = e.NumEstacao;
        p[i++].Value = V(e.NumAto);
        p[i++].Value = V(e.Tecnologia);
        p[i++].Value = Derive.Geracao(e.Tecnologia);
        p[i++].Value = V(e.Designacao);
        p[i++].Value = V(Derive.LarguraBanda(e.Designacao));
        p[i++].Value = V(e.FreqTx);
        p[i++].Value = V(e.FreqRx);
        p[i++].Value = V(Derive.Duplex(e.FreqTx, e.FreqRx));
        p[i++].Value = V(e.Azimute);
        p[i++].Value = V(e.PotenciaW);
        p[i++].Value = V(e.Ganho);
        p[i++].Value = V(e.Altura);
        p[i++].Value = V(e.DataLic);
        p[i++].Value = V(e.DataPrimLic);
        p[i++].Value = V(e.DataValidade);
        p[i++].Value = V(Derive.Vencida(e.DataValidade));
        p[i++].Value = V(Derive.AnosLicenciada(e.DataPrimLic));
        p[i++].Value = V(e.Status);
        p[i++].Value = V(e.Classe);
        p[i++].Value = V(e.Polarizacao);
        p[i++].Value = V(e.Elevacao);
        p[i++].Value = V(e.MeiaPotencia);
        p[i++].Value = V(e.FrenteCosta);
        p[i++].Value = V(e.CodTipoAntena);
        p[i++].Value = V(e.CodEquipAntena);
        p[i++].Value = V(e.CodEquipTx);
        p[i++].Value = V(Derive.PotenciaDbm(e.PotenciaW));
        p[i++].Value = V(Derive.Range(e.Azimute, 0, 360));
        p[i++].Value = V(Derive.Range(e.Elevacao, -90, 90));
        _emCmd.ExecuteNonQuery();
        Tick();
    }

    void Tick()
    {
        if (++_pending < 200_000) return;
        _tx.Commit();
        _tx.Dispose();
        _tx = _conn.BeginTransaction();
        _stCmd.Transaction = _tx;
        _emCmd.Transaction = _tx;
        _pending = 0;
    }

    public void Commit()
    {
        _tx.Commit();
        _tx.Dispose();
        _tx = _conn.BeginTransaction();
        _stCmd.Transaction = _tx;
        _emCmd.Transaction = _tx;
    }

    public void Dispose()
    {
        _tx.Dispose();
        _stCmd.Dispose();
        _emCmd.Dispose();
    }
}
