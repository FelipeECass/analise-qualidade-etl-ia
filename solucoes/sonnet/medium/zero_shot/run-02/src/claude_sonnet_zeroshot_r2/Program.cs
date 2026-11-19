using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Etl;

public static class Program
{
    const string Nome = "claude_sonnet_zeroshot_r2";
    static readonly DateTime Hoje = new(2026, 10, 15);

    public static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    static string Raiz()
    {
        const string win = @"D:\Projects\TCC";
        if (Directory.Exists(win)) return win;
        var env = Environment.GetEnvironmentVariable("TCC_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;
        return "/workspace";
    }

    static void Run()
    {
        var raiz = Raiz();
        var filesDir = Path.Combine(raiz, "files");
        var ddlPath = Path.Combine(raiz, "src", "DDL.sql");
        var dbDir = Path.Combine(raiz, "db");
        var dbPath = Path.Combine(dbDir, Nome + ".db");

        Directory.CreateDirectory(dbDir);
        foreach (var f in new[] { dbPath, dbPath + "-journal", dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);

        var zips = Directory.GetFiles(filesDir, "*.zip").OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        if (zips.Count == 0) throw new InvalidOperationException("Nenhum ZIP encontrado em " + filesDir);

        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        conn.Open();
        using (var p = conn.CreateCommand())
        {
            p.CommandText = "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;";
            p.ExecuteNonQuery();
        }
        using (var c = conn.CreateCommand())
        {
            c.CommandText = File.ReadAllText(ddlPath, Encoding.UTF8);
            c.ExecuteNonQuery();
        }

        var tx = conn.BeginTransaction();
        var cmdE = conn.CreateCommand();
        cmdE.Transaction = tx;
        cmdE.CommandText = @"INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora)
VALUES ($fistel,$num_estacao,$uf,$cod_municipio,$municipio,$endereco,$latitude,$longitude,$coordenada_valida,$entidade,$operadora)";
        string[] pe = { "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude", "coordenada_valida", "entidade", "operadora" };
        foreach (var n in pe) cmdE.Parameters.Add(new SqliteParameter("$" + n, null));

        string[] cols = {
            "uf","fistel","num_estacao","num_ato","tecnologia","geracao","designacao_emissao","largura_banda_khz",
            "freq_tx_mhz","freq_rx_mhz","duplex_mhz","azimute","potencia_w","ganho_antena","altura_antena_m",
            "data_licenciamento","data_primeiro_licenciamento","data_validade","vencida","anos_licenciada",
            "status","classe_estacao","polarizacao","angulo_elevacao_graus","angulo_meia_potencia_graus","frente_costa_db",
            "cod_tipo_antena","cod_equipamento_antena","cod_equipamento_transmissor","potencia_dbm","azimute_valido","elevacao_valida" };
        var cmdM = conn.CreateCommand();
        cmdM.Transaction = tx;
        cmdM.CommandText = $"INSERT INTO emissao ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select(c => "$" + c))})";
        foreach (var n in cols) cmdM.Parameters.Add(new SqliteParameter("$" + n, null));

        var estacoes = new HashSet<string>();
        var emissoes = new HashSet<string>();
        long lidas = 0, rejeitadas = 0, nE = 0, nM = 0;

        foreach (var zip in zips)
        {
            using var za = ZipFile.OpenRead(zip);
            var entry = za.Entries.FirstOrDefault(e => e.Length > 0 && !e.FullName.EndsWith('/'))
                ?? throw new InvalidDataException("ZIP sem arquivo: " + zip);
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);

            var header = ReadRecord(reader);
            if (header == null) continue;
            var idx = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < header.Count; i++)
            {
                var h = header[i].Trim().TrimStart('\uFEFF');
                idx.TryAdd(h, i);
            }

            List<string>? rec;
            while ((rec = ReadRecord(reader)) != null)
            {
                if (rec.Count == 1 && rec[0].Length == 0) continue; // linha em branco
                lidas++;
                string? Txt(string col)
                {
                    if (!idx.TryGetValue(col, out var i) || i >= rec.Count) return null;
                    var v = rec[i].Trim();
                    return v.Length == 0 ? null : v;
                }
                double? Num(string col) => ParseNum(Txt(col));
                string? Dt(string col) => ParseDate(Txt(col));

                var fistel = Txt("NumFistel");
                var numEst = Txt("NumEstacao");
                var uf = Txt("SiglaUf")?.ToUpperInvariant();
                if (fistel == null || numEst == null || uf == null) { rejeitadas++; continue; }

                var numAto = Txt("NumAto");
                var tecnologia = Txt("Tecnologia")?.ToUpperInvariant();
                var designacao = Txt("DesignacaoEmissao")?.ToUpperInvariant();
                var txF = Num("FreqTxMHz");
                var rxF = Num("FreqRxMHz");
                var azimute = Num("Azimute");
                var potW = Num("PotenciaTransmissorWatts");
                var elev = Num("AnguloElevacao");
                var dtVal = Dt("DataValidade");
                var dtPrim = Dt("DataPrimeiroLicenciamento");

                var keyE = uf + "\u0001" + fistel + "\u0001" + numEst;
                if (estacoes.Add(keyE))
                {
                    var entidade = NormalizaTexto(Txt("NomeEntidade"));
                    var lat = Num("Latitude");
                    var lon = Num("Longitude");
                    var coordOk = lat.HasValue && lon.HasValue && !(lat.Value == 0 && lon.Value == 0)
                        && lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
                    SetP(cmdE, "fistel", fistel);
                    SetP(cmdE, "num_estacao", numEst);
                    SetP(cmdE, "uf", uf);
                    SetP(cmdE, "cod_municipio", Txt("CodMunicipio"));
                    SetP(cmdE, "municipio", Txt("Municipio.NomeMunicipio"));
                    SetP(cmdE, "endereco", NormalizaTexto(Txt("EnderecoEstacao")));
                    SetP(cmdE, "latitude", lat);
                    SetP(cmdE, "longitude", lon);
                    SetP(cmdE, "coordenada_valida", coordOk ? 1 : 0);
                    SetP(cmdE, "entidade", entidade);
                    SetP(cmdE, "operadora", Operadora(entidade));
                    cmdE.ExecuteNonQuery();
                    nE++;
                }

                var keyM = string.Join("\u0001", keyE, numAto ?? "\u0002", Fmt(azimute), Fmt(txF), Fmt(rxF),
                    designacao ?? "\u0002", tecnologia ?? "\u0002");
                if (!emissoes.Add(keyM)) continue;

                double? duplex = null;
                if (txF.HasValue && rxF.HasValue)
                    {
                    var dd = double.Parse((txF.Value - rxF.Value).ToString("F3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    duplex = dd == 0 ? 0.0 : dd;
                }
                double? dbm = null;
                if (potW.HasValue && potW.Value > 0)
                    dbm = Math.Round(10 * Math.Log10(potW.Value * 1000), 1, MidpointRounding.AwayFromZero);
                double? anos = null;
                if (dtPrim != null)
                {
                    var d = DateTime.ParseExact(dtPrim, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    anos = (double)Math.Round((decimal)(Hoje - d).Days / 365.25m, 1, MidpointRounding.AwayFromZero);
                }
                bool vencida = dtVal != null && string.CompareOrdinal(dtVal, "2026-10-15") < 0;

                SetP(cmdM, "uf", uf);
                SetP(cmdM, "fistel", fistel);
                SetP(cmdM, "num_estacao", numEst);
                SetP(cmdM, "num_ato", numAto);
                SetP(cmdM, "tecnologia", tecnologia);
                SetP(cmdM, "geracao", Geracao(tecnologia));
                SetP(cmdM, "designacao_emissao", designacao);
                SetP(cmdM, "largura_banda_khz", Largura(designacao));
                SetP(cmdM, "freq_tx_mhz", txF);
                SetP(cmdM, "freq_rx_mhz", rxF);
                SetP(cmdM, "duplex_mhz", duplex);
                SetP(cmdM, "azimute", azimute);
                SetP(cmdM, "potencia_w", potW);
                SetP(cmdM, "ganho_antena", Num("GanhoAntena"));
                SetP(cmdM, "altura_antena_m", Num("AlturaAntena"));
                SetP(cmdM, "data_licenciamento", Dt("DataLicenciamento"));
                SetP(cmdM, "data_primeiro_licenciamento", dtPrim);
                SetP(cmdM, "data_validade", dtVal);
                SetP(cmdM, "vencida", vencida ? 1 : 0);
                SetP(cmdM, "anos_licenciada", anos);
                SetP(cmdM, "status", Txt("Status.state"));
                SetP(cmdM, "classe_estacao", Txt("CodTipoClasseEstacao"));
                SetP(cmdM, "polarizacao", Txt("Polarizacao")?.ToUpperInvariant());
                SetP(cmdM, "angulo_elevacao_graus", elev);
                SetP(cmdM, "angulo_meia_potencia_graus", Num("AnguloMeiaPotenciaAntena"));
                SetP(cmdM, "frente_costa_db", Num("FrenteCostaAntena"));
                SetP(cmdM, "cod_tipo_antena", Txt("CodTipoAntena"));
                SetP(cmdM, "cod_equipamento_antena", Txt("CodEquipamentoAntena"));
                SetP(cmdM, "cod_equipamento_transmissor", Txt("CodEquipamentoTransmissor"));
                SetP(cmdM, "potencia_dbm", dbm);
                SetP(cmdM, "azimute_valido", azimute.HasValue ? (azimute.Value >= 0 && azimute.Value <= 360 ? 1 : 0) : (int?)null);
                SetP(cmdM, "elevacao_valida", elev.HasValue ? (elev.Value >= -90 && elev.Value <= 90 ? 1 : 0) : (int?)null);
                cmdM.ExecuteNonQuery();
                nM++;
            }
            Console.WriteLine($"{Path.GetFileName(zip)}: estacoes={nE} emissoes={nM}");
        }

        tx.Commit();
        Console.WriteLine($"Linhas lidas={lidas} rejeitadas={rejeitadas} estacoes={nE} emissoes={nM}");
    }

    static void SetP(SqliteCommand c, string name, object? v) =>
        c.Parameters["$" + name].Value = v ?? DBNull.Value;

    static string Fmt(double? d) => d.HasValue ? d.Value.ToString("R", CultureInfo.InvariantCulture) : "\u0002";

    static double? ParseNum(string? s)
    {
        if (s == null) return null;
        s = s.Replace(',', '.');
        if (double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
            return d == 0 ? 0.0 : d; // evita -0
        return null;
    }

    static string? ParseDate(string? s)
    {
        if (s == null) return null;
        if (s.Length == 10 && DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return null;
    }

    // Fallback para quando a normalização Unicode não decompõe (modo invariante/sem ICU).
    const string Acentos = "ÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝàáâãäåçèéêëìíîïñòóôõöùúûüýÿ";
    const string Bases   = "AAAAAACEEEEIIIINOOOOOUUUUYaaaaaaceeeeiiiinooooouuuuyy";

    static string? NormalizaTexto(string? s)
    {
        if (s == null) return null;
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch0 in d)
        {
            var ch = ch0;
            if (ch >= '\u00C0' && ch < '\u0180')
            {
                var i = Acentos.IndexOf(ch);
                if (i >= 0) { sb.Append(Bases[i]); continue; }
            }
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch);
        }
        var up = sb.ToString().ToUpperInvariant();
        var o = new StringBuilder(up.Length);
        bool lastSpace = true;
        foreach (var ch in up)
        {
            if (char.IsLetterOrDigit(ch)) { o.Append(ch); lastSpace = false; }
            else if (!lastSpace) { o.Append(' '); lastSpace = true; }
        }
        var r = o.ToString().Trim();
        return r.Length == 0 ? null : r;
    }

    static string Operadora(string? e)
    {
        if (e == null) return "OUTRAS";
        var t = e.Split(' ');
        bool Has(string w) => Array.IndexOf(t, w) >= 0;
        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (t[0] == "TIM") return "TIM";
        if (t[0] == "OI" || Has("TELEMAR") || (" " + e + " ").Contains(" BRASIL TELECOM ")) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    static string Geracao(string? tec)
    {
        if (tec == null) return "N/D";
        switch (tec.ToUpperInvariant())
        {
            case "GSM": case "CDMA": return "2G";
            case "WCDMA": case "WDCMA": return "3G";
            case "LTE": return "4G";
            case "NR": return "5G";
            default: return "OUTRA";
        }
    }

    static double? Largura(string? des)
    {
        if (des == null || des.Length < 4) return null;
        var s = des.Substring(0, 4).ToUpperInvariant();
        int u = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = s[i];
            if (c >= '0' && c <= '9') continue;
            if ((c == 'H' || c == 'K' || c == 'M' || c == 'G') && i > 0 && u < 0) { u = i; continue; }
            return null;
        }
        if (u < 0) return null;
        var inteiro = s.Substring(0, u);
        var frac = s.Substring(u + 1);
        if (!decimal.TryParse(inteiro + (frac.Length > 0 ? "." + frac : ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v))
            return null;
        decimal fator = s[u] switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, _ => 1000000m };
        return (double)(v * fator);
    }

    // Lê um registro CSV com separador '|' e campos entre aspas (aspas duplicadas = escape).
    static List<string>? ReadRecord(StreamReader r)
    {
        int c = r.Read();
        if (c < 0) return null;
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQ = false;
        while (true)
        {
            if (c < 0) { fields.Add(sb.ToString()); return fields; }
            char ch = (char)c;
            if (inQ)
            {
                if (ch == '"')
                {
                    if (r.Peek() == '"') { r.Read(); sb.Append('"'); }
                    else inQ = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') inQ = true;
                else if (ch == '|') { fields.Add(sb.ToString()); sb.Clear(); }
                else if (ch == '\n') { fields.Add(sb.ToString()); return fields; }
                else if (ch == '\r') { /* ignora */ }
                else sb.Append(ch);
            }
            c = r.Read();
        }
    }
}
