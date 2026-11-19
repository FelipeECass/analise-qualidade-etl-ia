using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Etl;

public static class Program
{
    static readonly DateTime Hoje = new(2026, 10, 15);
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Main()
    {
        try
        {
            Run();
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
        const string win = @"D:\Projects\TCC";
        if (Directory.Exists(win)) return win;
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "files")) && File.Exists(Path.Combine(d.FullName, "src", "DDL.sql")))
                    return d.FullName;
                d = d.Parent;
            }
        }
        throw new DirectoryNotFoundException("Pasta raiz do projeto não encontrada.");
    }

    static void Run()
    {
        var root = FindRoot();
        var filesDir = Path.Combine(root, "files");
        var ddl = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"), Encoding.UTF8);
        var dbDir = Path.Combine(root, "db");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "claude_sonnet_zeroshot_r3.db");
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix);

        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;";
            pragma.ExecuteNonQuery();
        }
        using (var c = conn.CreateCommand())
        {
            c.CommandText = ddl;
            c.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();
        using var insEst = conn.CreateCommand();
        insEst.Transaction = tx;
        insEst.CommandText = @"INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora)
VALUES ($fistel,$num_estacao,$uf,$cod_municipio,$municipio,$endereco,$latitude,$longitude,$coordenada_valida,$entidade,$operadora)";
        var estNames = new[] { "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude", "coordenada_valida", "entidade", "operadora" };
        var estParams = AddParams(insEst, estNames);

        using var insEmi = conn.CreateCommand();
        insEmi.Transaction = tx;
        var emiNames = new[] { "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao", "largura_banda_khz",
            "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w", "ganho_antena", "altura_antena_m",
            "data_licenciamento", "data_primeiro_licenciamento", "data_validade", "vencida", "anos_licenciada",
            "status", "classe_estacao", "polarizacao", "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db",
            "cod_tipo_antena", "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido", "elevacao_valida" };
        insEmi.CommandText = "INSERT INTO emissao (" + string.Join(",", emiNames) + ") VALUES (" + string.Join(",", emiNames.Select(n => "$" + n)) + ")";
        var emiParams = AddParams(insEmi, emiNames);

        var estacoes = new HashSet<string>();
        var emissoes = new HashSet<string>();
        long nEst = 0, nEmi = 0, nLidas = 0, nRej = 0, nDup = 0;

        foreach (var zip in Directory.GetFiles(filesDir, "*.zip").OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
        {
            using var za = ZipFile.OpenRead(zip);
            var entry = za.Entries.FirstOrDefault(e => e.Length > 0 && !e.FullName.EndsWith('/'))
                ?? throw new InvalidDataException("ZIP sem arquivo: " + zip);
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);

            var headerRec = ReadRecord(reader);
            if (headerRec == null) continue;
            var idx = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < headerRec.Count; i++)
            {
                var h = headerRec[i].Trim().TrimStart('\uFEFF');
                idx.TryAdd(h, i);
            }

            string? Get(List<string> rec, string name)
            {
                if (!idx.TryGetValue(name, out var i) || i >= rec.Count) return null;
                var v = rec[i].Trim();
                return v.Length == 0 ? null : v;
            }

            List<string>? r;
            while ((r = ReadRecord(reader)) != null)
            {
                nLidas++;
                var fistel = Get(r, "NumFistel");
                var numEst = Get(r, "NumEstacao");
                var uf = Get(r, "SiglaUf")?.ToUpperInvariant();
                if (fistel == null || numEst == null || uf == null) { nRej++; continue; }

                var numAto = Get(r, "NumAto");
                var entidade = NormText(Get(r, "NomeEntidade"));
                var tecnologia = Get(r, "Tecnologia")?.ToUpperInvariant();
                var designacao = Get(r, "DesignacaoEmissao")?.ToUpperInvariant();
                var freqTx = Num(Get(r, "FreqTxMHz"));
                var freqRx = Num(Get(r, "FreqRxMHz"));
                var azimute = Num(Get(r, "Azimute"));

                var stKey = uf + "\u0001" + fistel + "\u0001" + numEst;
                if (estacoes.Add(stKey))
                {
                    var lat = Num(Get(r, "Latitude"));
                    var lon = Num(Get(r, "Longitude"));
                    bool coordOk = lat.HasValue && lon.HasValue && !(lat == 0 && lon == 0)
                        && lat >= -33.75 && lat <= 5.28 && lon >= -73.99 && lon <= -28.83;
                    SetP(estParams[0], fistel);
                    SetP(estParams[1], numEst);
                    SetP(estParams[2], uf);
                    SetP(estParams[3], Get(r, "CodMunicipio"));
                    SetP(estParams[4], Get(r, "Municipio.NomeMunicipio"));
                    SetP(estParams[5], NormText(Get(r, "EnderecoEstacao")));
                    SetP(estParams[6], lat);
                    SetP(estParams[7], lon);
                    SetP(estParams[8], coordOk ? 1 : 0);
                    SetP(estParams[9], entidade);
                    SetP(estParams[10], Operadora(entidade));
                    insEst.ExecuteNonQuery();
                    nEst++;
                }

                var dupKey = string.Join("\u0001", new[] { stKey, K(numAto), K(azimute), K(freqTx), K(freqRx), K(designacao), K(tecnologia) });
                if (!emissoes.Add(dupKey)) { nDup++; continue; }

                var potW = Num(Get(r, "PotenciaTransmissorWatts"));
                var dtLic = Date(Get(r, "DataLicenciamento"));
                var dtPrim = Date(Get(r, "DataPrimeiroLicenciamento"));
                var dtVal = Date(Get(r, "DataValidade"));
                var elev = Num(Get(r, "AnguloElevacao"));

                double? duplex = null;
                if (freqTx.HasValue && freqRx.HasValue)
                    duplex = RoundDec(ToDec(freqTx.Value) - ToDec(freqRx.Value), 3);
                double? dbm = null;
                if (potW.HasValue && potW.Value > 0)
                    dbm = Math.Round(10 * Math.Log10(potW.Value * 1000), 1, MidpointRounding.AwayFromZero);
                double? anos = null;
                if (dtPrim.HasValue)
                    anos = Math.Round((Hoje - dtPrim.Value).Days / 365.25, 1, MidpointRounding.AwayFromZero);

                int i2 = 0;
                SetP(emiParams[i2++], uf);
                SetP(emiParams[i2++], fistel);
                SetP(emiParams[i2++], numEst);
                SetP(emiParams[i2++], numAto);
                SetP(emiParams[i2++], tecnologia);
                SetP(emiParams[i2++], Geracao(tecnologia));
                SetP(emiParams[i2++], designacao);
                SetP(emiParams[i2++], Largura(designacao));
                SetP(emiParams[i2++], freqTx);
                SetP(emiParams[i2++], freqRx);
                SetP(emiParams[i2++], duplex);
                SetP(emiParams[i2++], azimute);
                SetP(emiParams[i2++], potW);
                SetP(emiParams[i2++], Num(Get(r, "GanhoAntena")));
                SetP(emiParams[i2++], Num(Get(r, "AlturaAntena")));
                SetP(emiParams[i2++], dtLic?.ToString("yyyy-MM-dd", Inv));
                SetP(emiParams[i2++], dtPrim?.ToString("yyyy-MM-dd", Inv));
                SetP(emiParams[i2++], dtVal?.ToString("yyyy-MM-dd", Inv));
                SetP(emiParams[i2++], dtVal.HasValue && dtVal.Value < Hoje ? 1 : 0);
                SetP(emiParams[i2++], anos);
                SetP(emiParams[i2++], Get(r, "Status.state"));
                SetP(emiParams[i2++], Get(r, "CodTipoClasseEstacao"));
                SetP(emiParams[i2++], Get(r, "Polarizacao")?.ToUpperInvariant());
                SetP(emiParams[i2++], elev);
                SetP(emiParams[i2++], Num(Get(r, "AnguloMeiaPotenciaAntena")));
                SetP(emiParams[i2++], Num(Get(r, "FrenteCostaAntena")));
                SetP(emiParams[i2++], Get(r, "CodTipoAntena"));
                SetP(emiParams[i2++], Get(r, "CodEquipamentoAntena"));
                SetP(emiParams[i2++], Get(r, "CodEquipamentoTransmissor"));
                SetP(emiParams[i2++], dbm);
                SetP(emiParams[i2++], azimute.HasValue ? (azimute >= 0 && azimute <= 360 ? 1 : 0) : (int?)null);
                SetP(emiParams[i2++], elev.HasValue ? (elev >= -90 && elev <= 90 ? 1 : 0) : (int?)null);
                insEmi.ExecuteNonQuery();
                nEmi++;
            }
            Console.WriteLine($"{Path.GetFileName(zip)}: ok");
        }

        tx.Commit();
        Console.WriteLine($"Linhas lidas={nLidas} rejeitadas={nRej} duplicadas={nDup} estacoes={nEst} emissoes={nEmi}");
    }

    static SqliteParameter[] AddParams(SqliteCommand cmd, string[] names)
    {
        var arr = new SqliteParameter[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            arr[i] = cmd.CreateParameter();
            arr[i].ParameterName = "$" + names[i];
            cmd.Parameters.Add(arr[i]);
        }
        return arr;
    }

    static void SetP(SqliteParameter p, object? v) => p.Value = v ?? DBNull.Value;

    static string K(object? v) => v switch
    {
        null => "\u0002N",
        double d => d.ToString("R", Inv),
        _ => "V" + v
    };

    // ---- CSV ----
    static List<string>? ReadRecord(StreamReader rd)
    {
        int c = rd.Read();
        while (c == '\r' || c == '\n') c = rd.Read();
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
                    if (rd.Peek() == '"') { rd.Read(); sb.Append('"'); }
                    else inQ = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') inQ = true;
                else if (ch == '|') { fields.Add(sb.ToString()); sb.Clear(); }
                else if (ch == '\n') { fields.Add(sb.ToString()); return fields; }
                else if (ch == '\r') { }
                else sb.Append(ch);
            }
            c = rd.Read();
        }
    }

    // ---- Limpeza ----
    static double? Num(string? s)
    {
        if (s == null) return null;
        s = s.Replace(',', '.');
        if (!double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, Inv, out var d)) return null;
        return double.IsFinite(d) ? d : null;
    }

    static DateTime? Date(string? s)
    {
        if (s == null) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : null;
    }

    static string? NormText(string? s)
    {
        if (s == null) return null;
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var u = char.ToUpperInvariant(ch);
            sb.Append(char.IsLetterOrDigit(u) ? u : ' ');
        }
        var parts = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : string.Join(' ', parts);
    }

    static decimal ToDec(double d) => decimal.Parse(d.ToString("R", Inv), NumberStyles.Float, Inv);

    static double RoundDec(decimal d, int n) => (double)Math.Round(d, n, MidpointRounding.AwayFromZero);

    // ---- Derivadas ----
    static string Operadora(string? ent)
    {
        if (ent == null) return "OUTRAS";
        var words = ent.Split(' ');
        bool Has(string w) => Array.IndexOf(words, w) >= 0;
        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (words[0] == "TIM") return "TIM";
        if (words[0] == "OI" || Has("TELEMAR") || (" " + ent + " ").Contains(" BRASIL TELECOM ")) return "OI";
        if (Has("ALGAR")) return "ALGAR";
        if (Has("SERCOMTEL")) return "SERCOMTEL";
        if (Has("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }

    static string Geracao(string? tec)
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

    static double? Largura(string? des)
    {
        if (des == null || des.Length < 4) return null;
        var s = des.Substring(0, 4).ToUpperInvariant();
        int pos = -1;
        for (int i = 0; i < 4; i++)
        {
            if (char.IsAsciiDigit(s[i])) continue;
            if (pos >= 0 || i == 0) return null;
            if (s[i] is 'H' or 'K' or 'M' or 'G') pos = i; else return null;
        }
        if (pos < 0) return null;
        var inteiro = s.Substring(0, pos);
        var frac = s.Substring(pos + 1);
        decimal v = decimal.Parse(inteiro + (frac.Length > 0 ? "." + frac : ""), NumberStyles.Float, Inv);
        decimal f = s[pos] switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, _ => 1000000m };
        return (double)(v * f);
    }
}
