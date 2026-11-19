using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Etl;

public static class Program
{
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
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
    }

    static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable("TCC_ROOT");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        if (Directory.Exists(@"D:\Projects\TCC")) return @"D:\Projects\TCC";
        // Fallback: sobe a partir do diretório do executável até achar src/DDL.sql
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Pasta raiz do projeto TCC não encontrada.");
    }

    static void Run()
    {
        string root = ResolveRoot();
        string filesDir = Path.Combine(root, "files");
        string ddlPath = Path.Combine(root, "src", "DDL.sql");
        string dbDir = Path.Combine(root, "db");
        string dbPath = Path.Combine(dbDir, "claude_sonnet_zeroshot_r1.db");

        Directory.CreateDirectory(dbDir);
        foreach (var f in new[] { dbPath, dbPath + "-journal", dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);

        var zips = Directory.GetFiles(filesDir, "*.zip").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (zips.Length == 0) throw new FileNotFoundException("Nenhum ZIP encontrado em " + filesDir);

        using var conn = new SqliteConnection(dbPath);
        conn.Open();
        conn.Exec("PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;");
        conn.Exec(File.ReadAllText(ddlPath, Encoding.UTF8));

        using var tx = conn.BeginTransaction();
        var insEst = conn.CreateCommand();
        insEst.Transaction = tx;
        insEst.CommandText =
            "INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) " +
            "VALUES ($fistel,$num_estacao,$uf,$cod_municipio,$municipio,$endereco,$latitude,$longitude,$coord,$entidade,$operadora)";
        var estParams = new[] { "$fistel", "$num_estacao", "$uf", "$cod_municipio", "$municipio", "$endereco", "$latitude", "$longitude", "$coord", "$entidade", "$operadora" };
        foreach (var p in estParams) insEst.Parameters.Add(new SqliteParameter(p, null));

        var insEmi = conn.CreateCommand();
        insEmi.Transaction = tx;
        string[] emiCols =
        {
            "uf","fistel","num_estacao","num_ato","tecnologia","geracao","designacao_emissao","largura_banda_khz",
            "freq_tx_mhz","freq_rx_mhz","duplex_mhz","azimute","potencia_w","ganho_antena","altura_antena_m",
            "data_licenciamento","data_primeiro_licenciamento","data_validade","vencida","anos_licenciada",
            "status","classe_estacao","polarizacao","angulo_elevacao_graus","angulo_meia_potencia_graus",
            "frente_costa_db","cod_tipo_antena","cod_equipamento_antena","cod_equipamento_transmissor",
            "potencia_dbm","azimute_valido","elevacao_valida"
        };
        insEmi.CommandText = $"INSERT INTO emissao ({string.Join(",", emiCols)}) VALUES ({string.Join(",", emiCols.Select(c => "$" + c))})";
        foreach (var c in emiCols) insEmi.Parameters.Add(new SqliteParameter("$" + c, null));

        var estacoes = new HashSet<string>();
        var emissoes = new HashSet<string>();
        long totalLinhas = 0, rejeitadas = 0, nEst = 0, nEmi = 0;

        foreach (var zipPath in zips)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name))
                ?? throw new InvalidDataException("ZIP vazio: " + zipPath);
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);
            var csv = new CsvReader(reader);

            var header = csv.ReadRecord();
            if (header == null) continue;
            var idx = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < header.Count; i++)
            {
                var name = header[i].Trim().TrimStart('\uFEFF');
                idx.TryAdd(name, i);
            }

            string? Get(List<string> rec, string col)
            {
                if (!idx.TryGetValue(col, out int i) || i >= rec.Count) return null;
                var v = rec[i].Trim();
                return v.Length == 0 ? null : v;
            }

            List<string>? r;
            while ((r = csv.ReadRecord()) != null)
            {
                if (r.Count == 1 && r[0].Length == 0) continue; // linha em branco
                totalLinhas++;

                string? fistel = Get(r, "NumFistel");
                string? numEstacao = Get(r, "NumEstacao");
                string? uf = Get(r, "SiglaUf")?.ToUpperInvariant();
                if (fistel == null || numEstacao == null || uf == null) { rejeitadas++; continue; }

                string? numAto = Get(r, "NumAto");
                string? entidade = NormalizeText(Get(r, "NomeEntidade"));
                double? lat = ParseNum(Get(r, "Latitude"));
                double? lon = ParseNum(Get(r, "Longitude"));
                string? tecnologia = Get(r, "Tecnologia")?.ToUpperInvariant();
                string? designacao = Get(r, "DesignacaoEmissao")?.ToUpperInvariant();
                double? fTx = ParseNum(Get(r, "FreqTxMHz"));
                double? fRx = ParseNum(Get(r, "FreqRxMHz"));
                double? azimute = ParseNum(Get(r, "Azimute"));

                string estKey = uf + "\u0001" + fistel + "\u0001" + numEstacao;
                if (estacoes.Add(estKey))
                {
                    bool coordOk = lat.HasValue && lon.HasValue && !(lat.Value == 0 && lon.Value == 0)
                        && lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
                    insEst.Parameters["$fistel"].Value = fistel;
                    insEst.Parameters["$num_estacao"].Value = numEstacao;
                    insEst.Parameters["$uf"].Value = uf;
                    insEst.Parameters["$cod_municipio"].Value = Db(Get(r, "CodMunicipio"));
                    insEst.Parameters["$municipio"].Value = Db(Get(r, "Municipio.NomeMunicipio"));
                    insEst.Parameters["$endereco"].Value = Db(NormalizeText(Get(r, "EnderecoEstacao")));
                    insEst.Parameters["$latitude"].Value = Db(lat);
                    insEst.Parameters["$longitude"].Value = Db(lon);
                    insEst.Parameters["$coord"].Value = coordOk ? 1 : 0;
                    insEst.Parameters["$entidade"].Value = Db(entidade);
                    insEst.Parameters["$operadora"].Value = Operadora(entidade);
                    insEst.ExecuteNonQuery();
                    nEst++;
                }

                string emiKey = string.Join("\u0001", uf, fistel, numEstacao, K(numAto), K(azimute), K(fTx), K(fRx), K(designacao), K(tecnologia));
                if (!emissoes.Add(emiKey)) continue;

                double? potencia = ParseNum(Get(r, "PotenciaTransmissorWatts"));
                double? elevacao = ParseNum(Get(r, "AnguloElevacao"));
                DateTime? dLic = ParseDate(Get(r, "DataLicenciamento"));
                DateTime? dPrim = ParseDate(Get(r, "DataPrimeiroLicenciamento"));
                DateTime? dVal = ParseDate(Get(r, "DataValidade"));

                double? duplex = null;
                if (fTx.HasValue && fRx.HasValue)
                    duplex = (double)Math.Round((decimal)fTx.Value - (decimal)fRx.Value, 3, MidpointRounding.AwayFromZero);

                double? dbm = null;
                if (potencia.HasValue && potencia.Value > 0)
                    dbm = Math.Round(10 * Math.Log10(potencia.Value * 1000), 1, MidpointRounding.AwayFromZero);

                double? anos = null;
                if (dPrim.HasValue)
                {
                    decimal dias = (decimal)(Hoje - dPrim.Value).Days;
                    anos = (double)Math.Round(dias / 365.25m, 1, MidpointRounding.AwayFromZero);
                }

                var P = insEmi.Parameters;
                P["$uf"].Value = uf;
                P["$fistel"].Value = fistel;
                P["$num_estacao"].Value = numEstacao;
                P["$num_ato"].Value = Db(numAto);
                P["$tecnologia"].Value = Db(tecnologia);
                P["$geracao"].Value = Geracao(tecnologia);
                P["$designacao_emissao"].Value = Db(designacao);
                P["$largura_banda_khz"].Value = Db(Largura(designacao));
                P["$freq_tx_mhz"].Value = Db(fTx);
                P["$freq_rx_mhz"].Value = Db(fRx);
                P["$duplex_mhz"].Value = Db(duplex);
                P["$azimute"].Value = Db(azimute);
                P["$potencia_w"].Value = Db(potencia);
                P["$ganho_antena"].Value = Db(ParseNum(Get(r, "GanhoAntena")));
                P["$altura_antena_m"].Value = Db(ParseNum(Get(r, "AlturaAntena")));
                P["$data_licenciamento"].Value = Db(dLic);
                P["$data_primeiro_licenciamento"].Value = Db(dPrim);
                P["$data_validade"].Value = Db(dVal);
                P["$vencida"].Value = (dVal.HasValue && dVal.Value < Hoje) ? 1 : 0;
                P["$anos_licenciada"].Value = Db(anos);
                P["$status"].Value = Db(Get(r, "Status.state"));
                P["$classe_estacao"].Value = Db(Get(r, "CodTipoClasseEstacao"));
                P["$polarizacao"].Value = Db(Get(r, "Polarizacao")?.ToUpperInvariant());
                P["$angulo_elevacao_graus"].Value = Db(elevacao);
                P["$angulo_meia_potencia_graus"].Value = Db(ParseNum(Get(r, "AnguloMeiaPotenciaAntena")));
                P["$frente_costa_db"].Value = Db(ParseNum(Get(r, "FrenteCostaAntena")));
                P["$cod_tipo_antena"].Value = Db(Get(r, "CodTipoAntena"));
                P["$cod_equipamento_antena"].Value = Db(Get(r, "CodEquipamentoAntena"));
                P["$cod_equipamento_transmissor"].Value = Db(Get(r, "CodEquipamentoTransmissor"));
                P["$potencia_dbm"].Value = Db(dbm);
                P["$azimute_valido"].Value = azimute.HasValue ? (azimute.Value >= 0 && azimute.Value <= 360 ? 1 : 0) : DBNull.Value;
                P["$elevacao_valida"].Value = elevacao.HasValue ? (elevacao.Value >= -90 && elevacao.Value <= 90 ? 1 : 0) : DBNull.Value;
                insEmi.ExecuteNonQuery();
                nEmi++;
            }
            Console.WriteLine($"{Path.GetFileName(zipPath)}: ok (estações={nEst}, emissões={nEmi})");
        }

        tx.Commit();
        Console.WriteLine($"Linhas lidas={totalLinhas}, rejeitadas={rejeitadas}, estações={nEst}, emissões={nEmi}");
    }

    // ---------- helpers ----------

    static object Db(string? v) => v == null ? DBNull.Value : v;
    static object Db(double? v) => v.HasValue ? v.Value : DBNull.Value;
    static object Db(DateTime? v) => v.HasValue ? v.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;
    static string K(string? s) => s == null ? "\u0002N" : "S" + s;
    static string K(double? d) => d.HasValue ? "D" + d.Value.ToString("R", CultureInfo.InvariantCulture) : "\u0002N";

    static double? ParseNum(string? s)
    {
        if (s == null) return null;
        s = s.Trim().Replace(',', '.');
        if (s.Length == 0) return null;
        if (!double.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out double d)) return null;
        if (!double.IsFinite(d)) return null;
        if (d == 0) d = 0; // normaliza -0
        return d;
    }

    static DateTime? ParseDate(string? s)
    {
        if (s == null) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    static string? NormalizeText(string? s)
    {
        if (s == null) return null;
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch);
        }
        var up = sb.ToString().ToUpperInvariant();
        sb.Clear();
        bool lastSpace = true;
        foreach (var ch in up)
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); lastSpace = false; }
            else if (!lastSpace) { sb.Append(' '); lastSpace = true; }
        }
        var res = sb.ToString().Trim();
        return res.Length == 0 ? null : res;
    }

    static string Operadora(string? ent)
    {
        if (ent == null) return "OUTRAS";
        var w = ent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Has(string x) => Array.IndexOf(w, x) >= 0;
        if (Has("TELEFONICA") || Has("VIVO")) return "VIVO";
        if (Has("CLARO") || Has("EMBRATEL")) return "CLARO";
        if (w.Length > 0 && w[0] == "TIM") return "TIM";
        if ((w.Length > 0 && w[0] == "OI") || Has("TELEMAR") || (" " + ent + " ").Contains(" BRASIL TELECOM ")) return "OI";
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
        if (des == null) return null;
        des = des.ToUpperInvariant();
        if (des.Length < 4) return null;
        string p = des.Substring(0, 4);
        int u = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = p[i];
            if (c >= '0' && c <= '9') continue;
            if (c == 'H' || c == 'K' || c == 'M' || c == 'G')
            {
                if (i == 0 || u >= 0) return null;
                u = i;
            }
            else return null;
        }
        if (u < 0) return null;
        string ip = p.Substring(0, u), fp = p.Substring(u + 1);
        decimal v = decimal.Parse(fp.Length == 0 ? ip : ip + "." + fp, CultureInfo.InvariantCulture);
        decimal factor = p[u] switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, _ => 1000000m };
        return (double)(v * factor);
    }
}

sealed class CsvReader
{
    readonly TextReader _r;
    public CsvReader(TextReader r) { _r = r; }

    // Separador '|', campos opcionalmente entre aspas, "" = aspas literais.
    public List<string>? ReadRecord()
    {
        int c = _r.Read();
        if (c < 0) return null;
        var rec = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        while (true)
        {
            if (c < 0) { rec.Add(sb.ToString()); return rec; }
            char ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (_r.Peek() == '"') { _r.Read(); sb.Append('"'); }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == '|') { rec.Add(sb.ToString()); sb.Clear(); }
                else if (ch == '\n') { rec.Add(sb.ToString()); return rec; }
                else if (ch == '\r') { /* ignora */ }
                else sb.Append(ch);
            }
            c = _r.Read();
        }
    }
}
