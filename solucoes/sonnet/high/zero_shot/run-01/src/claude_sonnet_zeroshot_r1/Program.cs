using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace EtlAnatel;

internal static class Program
{
    private const string NomeProjeto = "claude_sonnet_zeroshot_r1";
    private static readonly DateTime Hoje = new(2026, 10, 15);

    private static int Main()
    {
        try
        {
            Executar();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
    }

    // ---------------------------------------------------------------- caminhos

    private static string LocalizarRaiz()
    {
        const string windows = @"D:\Projects\TCC";
        if (Directory.Exists(Path.Combine(windows, "files")))
            return windows;

        foreach (var inicio in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(inicio);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "files")) &&
                    File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException("Pasta raiz do projeto (com 'files' e 'src/DDL.sql') não encontrada.");
    }

    // ---------------------------------------------------------------- principal

    private static void Executar()
    {
        var raiz = LocalizarRaiz();
        var pastaZips = Path.Combine(raiz, "files");
        var ddlPath = Path.Combine(raiz, "src", "DDL.sql");
        var pastaDb = Path.Combine(raiz, "db");
        var dbPath = Path.Combine(pastaDb, NomeProjeto + ".db");

        Directory.CreateDirectory(pastaDb);
        foreach (var sufixo in new[] { "", "-journal", "-wal", "-shm" })
        {
            var f = dbPath + sufixo;
            if (File.Exists(f)) File.Delete(f);
        }

        var zips = Directory.GetFiles(pastaZips, "*.zip");
        Array.Sort(zips, StringComparer.OrdinalIgnoreCase);
        if (zips.Length == 0)
            throw new FileNotFoundException("Nenhum arquivo ZIP encontrado em " + pastaZips);

        var ddl = File.ReadAllText(ddlPath, Encoding.UTF8);

        var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        using var conn = new SqliteConnection(csb.ToString());
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF; PRAGMA temp_store = MEMORY;";
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();
        using var insEstacao = CriarInsertEstacao(conn, tx);
        using var insEmissao = CriarInsertEmissao(conn, tx);

        var estacoes = new HashSet<string>();
        var emissoes = new HashSet<string>();
        long lidas = 0, rejeitadas = 0, nEstacoes = 0, nEmissoes = 0, duplicadas = 0;

        foreach (var zip in zips)
        {
            Console.WriteLine("Processando " + Path.GetFileName(zip));
            using var arq = ZipFile.OpenRead(zip);
            var entrada = arq.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                          ?? arq.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name))
                          ?? throw new InvalidDataException("ZIP sem arquivo: " + zip);

            using var stream = entrada.Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);
            var csv = new CsvReader(reader);

            var cab = csv.ReadRecord() ?? throw new InvalidDataException("CSV vazio: " + zip);
            var cols = MapearColunas(cab);

            List<string>? rec;
            while ((rec = csv.ReadRecord()) != null)
            {
                lidas++;
                string? Txt(string nome) => cols.TryGetValue(nome, out var i) && i < rec.Count ? Limpar(rec[i]) : null;

                var fistel = Txt("NumFistel");
                var numEstacao = Txt("NumEstacao");
                var uf = Txt("SiglaUf")?.ToUpperInvariant();
                if (fistel == null || numEstacao == null || uf == null)
                {
                    rejeitadas++;
                    continue;
                }

                var numAto = Txt("NumAto");
                var tecnologia = Txt("Tecnologia")?.ToUpperInvariant();
                var designacao = Txt("DesignacaoEmissao")?.ToUpperInvariant();
                var freqTx = Num(Txt("FreqTxMHz"));
                var freqRx = Num(Txt("FreqRxMHz"));
                var azimute = Num(Txt("Azimute"));

                // Estação
                var chaveEst = uf + "\u001F" + fistel + "\u001F" + numEstacao;
                if (estacoes.Add(chaveEst))
                {
                    var entidade = Normalizar(Txt("NomeEntidade"));
                    var lat = Num(Txt("Latitude"));
                    var lon = Num(Txt("Longitude"));
                    Bind(insEstacao, 0, fistel);
                    Bind(insEstacao, 1, numEstacao);
                    Bind(insEstacao, 2, uf);
                    Bind(insEstacao, 3, Txt("CodMunicipio"));
                    Bind(insEstacao, 4, Txt("Municipio.NomeMunicipio"));
                    Bind(insEstacao, 5, Normalizar(Txt("EnderecoEstacao")));
                    Bind(insEstacao, 6, lat);
                    Bind(insEstacao, 7, lon);
                    Bind(insEstacao, 8, CoordenadaValida(lat, lon));
                    Bind(insEstacao, 9, entidade);
                    Bind(insEstacao, 10, Operadora(entidade));
                    insEstacao.ExecuteNonQuery();
                    nEstacoes++;
                }

                // Emissão
                var chaveEmi = string.Join('\u001F',
                    chaveEst, Chave(numAto), Chave(azimute), Chave(freqTx), Chave(freqRx), Chave(designacao), Chave(tecnologia));
                if (!emissoes.Add(chaveEmi))
                {
                    duplicadas++;
                    continue;
                }

                var potencia = Num(Txt("PotenciaTransmissorWatts"));
                var elevacao = Num(Txt("AnguloElevacao"));
                var dataLic = Data(Txt("DataLicenciamento"));
                var dataPrim = Data(Txt("DataPrimeiroLicenciamento"));
                var dataVal = Data(Txt("DataValidade"));

                Bind(insEmissao, 0, uf);
                Bind(insEmissao, 1, fistel);
                Bind(insEmissao, 2, numEstacao);
                Bind(insEmissao, 3, numAto);
                Bind(insEmissao, 4, tecnologia);
                Bind(insEmissao, 5, Geracao(tecnologia));
                Bind(insEmissao, 6, designacao);
                Bind(insEmissao, 7, LarguraBanda(designacao));
                Bind(insEmissao, 8, freqTx);
                Bind(insEmissao, 9, freqRx);
                Bind(insEmissao, 10, Duplex(freqTx, freqRx));
                Bind(insEmissao, 11, azimute);
                Bind(insEmissao, 12, potencia);
                Bind(insEmissao, 13, Num(Txt("GanhoAntena")));
                Bind(insEmissao, 14, Num(Txt("AlturaAntena")));
                Bind(insEmissao, 15, FormatarData(dataLic));
                Bind(insEmissao, 16, FormatarData(dataPrim));
                Bind(insEmissao, 17, FormatarData(dataVal));
                Bind(insEmissao, 18, dataVal.HasValue && dataVal.Value < Hoje);
                Bind(insEmissao, 19, AnosLicenciada(dataPrim));
                Bind(insEmissao, 20, Txt("Status.state"));
                Bind(insEmissao, 21, Txt("CodTipoClasseEstacao"));
                Bind(insEmissao, 22, Txt("Polarizacao")?.ToUpperInvariant());
                Bind(insEmissao, 23, elevacao);
                Bind(insEmissao, 24, Num(Txt("AnguloMeiaPotenciaAntena")));
                Bind(insEmissao, 25, Num(Txt("FrenteCostaAntena")));
                Bind(insEmissao, 26, Txt("CodTipoAntena"));
                Bind(insEmissao, 27, Txt("CodEquipamentoAntena"));
                Bind(insEmissao, 28, Txt("CodEquipamentoTransmissor"));
                Bind(insEmissao, 29, PotenciaDbm(potencia));
                Bind(insEmissao, 30, azimute.HasValue ? (bool?)(azimute.Value >= 0 && azimute.Value <= 360) : null);
                Bind(insEmissao, 31, elevacao.HasValue ? (bool?)(elevacao.Value >= -90 && elevacao.Value <= 90) : null);
                insEmissao.ExecuteNonQuery();
                nEmissoes++;
            }
        }

        tx.Commit();
        Console.WriteLine($"Linhas lidas: {lidas}; rejeitadas: {rejeitadas}; emissões duplicadas: {duplicadas}");
        Console.WriteLine($"Estações: {nEstacoes}; emissões: {nEmissoes}");
        Console.WriteLine("Banco gerado em " + dbPath);
    }

    // ---------------------------------------------------------------- SQLite

    private static SqliteCommand CriarInsertEstacao(SqliteConnection conn, SqliteTransaction tx)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO estacao (fistel, num_estacao, uf, cod_municipio, municipio, endereco, latitude, longitude, coordenada_valida, entidade, operadora) " +
            "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10)";
        for (int i = 0; i <= 10; i++) cmd.Parameters.Add(new SqliteParameter("$p" + i, null));
        cmd.Prepare();
        return cmd;
    }

    private static SqliteCommand CriarInsertEmissao(SqliteConnection conn, SqliteTransaction tx)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO emissao (uf, fistel, num_estacao, num_ato, tecnologia, geracao, designacao_emissao, largura_banda_khz, " +
            "freq_tx_mhz, freq_rx_mhz, duplex_mhz, azimute, potencia_w, ganho_antena, altura_antena_m, data_licenciamento, " +
            "data_primeiro_licenciamento, data_validade, vencida, anos_licenciada, status, classe_estacao, polarizacao, " +
            "angulo_elevacao_graus, angulo_meia_potencia_graus, frente_costa_db, cod_tipo_antena, cod_equipamento_antena, " +
            "cod_equipamento_transmissor, potencia_dbm, azimute_valido, elevacao_valida) VALUES (" +
            string.Join(",", Enumerable.Range(0, 32).Select(i => "$p" + i)) + ")";
        for (int i = 0; i < 32; i++) cmd.Parameters.Add(new SqliteParameter("$p" + i, null));
        cmd.Prepare();
        return cmd;
    }

    private static void Bind(SqliteCommand cmd, int i, string? v) => cmd.Parameters[i].Value = v == null ? DBNull.Value : v;
    private static void Bind(SqliteCommand cmd, int i, double? v) => cmd.Parameters[i].Value = v.HasValue ? v.Value : DBNull.Value;
    private static void Bind(SqliteCommand cmd, int i, bool? v) => cmd.Parameters[i].Value = v.HasValue ? (v.Value ? 1L : 0L) : DBNull.Value;

    // ---------------------------------------------------------------- limpeza

    private static Dictionary<string, int> MapearColunas(List<string> cab)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < cab.Count; i++)
        {
            var nome = cab[i].Trim().TrimStart('\uFEFF');
            d.TryAdd(nome, i);
        }
        foreach (var obrig in new[] { "NumFistel", "NumEstacao", "SiglaUf" })
            if (!d.ContainsKey(obrig))
                throw new InvalidDataException("Coluna obrigatória ausente no cabeçalho: " + obrig);
        return d;
    }

    private static string? Limpar(string? s)
    {
        if (s == null) return null;
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    private static double? Num(string? s)
    {
        if (s == null) return null;
        s = s.Replace(',', '.');
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v))
            return v == 0 ? 0.0 : v; // normaliza -0
        return null;
    }

    private static DateTime? Data(string? s)
    {
        if (s == null || s.Length != 10) return null;
        for (int i = 0; i < 10; i++)
        {
            if (i == 4 || i == 7) { if (s[i] != '-') return null; }
            else if (s[i] < '0' || s[i] > '9') return null;
        }
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string? FormatarData(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Normalizar(string? s)
    {
        if (s == null) return null;
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        bool espaco = false;
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var up = char.ToUpperInvariant(ch);
            if (char.IsLetterOrDigit(up))
            {
                if (espaco && sb.Length > 0) sb.Append(' ');
                espaco = false;
                sb.Append(up);
            }
            else espaco = true;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string Chave(string? s) => s == null ? "\u0000" : "S" + s;
    private static string Chave(double? v) => v.HasValue ? "N" + BitConverter.DoubleToInt64Bits(v.Value).ToString(CultureInfo.InvariantCulture) : "\u0000";

    // ---------------------------------------------------------------- derivações

    private static readonly Regex RxVivo = new(@"\b(TELEFONICA|VIVO)\b", RegexOptions.CultureInvariant);
    private static readonly Regex RxClaro = new(@"\b(CLARO|EMBRATEL)\b", RegexOptions.CultureInvariant);
    private static readonly Regex RxTim = new(@"^TIM\b", RegexOptions.CultureInvariant);
    private static readonly Regex RxOi = new(@"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b", RegexOptions.CultureInvariant);
    private static readonly Regex RxAlgar = new(@"\bALGAR\b", RegexOptions.CultureInvariant);
    private static readonly Regex RxSercomtel = new(@"\bSERCOMTEL\b", RegexOptions.CultureInvariant);
    private static readonly Regex RxBrisanet = new(@"\bBRISANET\b", RegexOptions.CultureInvariant);

    private static string Operadora(string? e)
    {
        if (e == null) return "OUTRAS";
        if (RxVivo.IsMatch(e)) return "VIVO";
        if (RxClaro.IsMatch(e)) return "CLARO";
        if (RxTim.IsMatch(e)) return "TIM";
        if (RxOi.IsMatch(e)) return "OI";
        if (RxAlgar.IsMatch(e)) return "ALGAR";
        if (RxSercomtel.IsMatch(e)) return "SERCOMTEL";
        if (RxBrisanet.IsMatch(e)) return "BRISANET";
        return "OUTRAS";
    }

    private static string Geracao(string? tec)
    {
        if (string.IsNullOrEmpty(tec)) return "N/D";
        return tec.ToUpperInvariant() switch
        {
            "GSM" or "CDMA" => "2G",
            "WCDMA" or "WDCMA" => "3G",
            "LTE" => "4G",
            "NR" => "5G",
            _ => "OUTRA"
        };
    }

    private static double? LarguraBanda(string? designacao)
    {
        if (designacao == null || designacao.Length < 4) return null;
        var p = designacao.Substring(0, 4).ToUpperInvariant();
        int pos = -1;
        for (int i = 0; i < 4; i++)
        {
            char c = p[i];
            if (c >= '0' && c <= '9') continue;
            if (c is 'H' or 'K' or 'M' or 'G' && i > 0 && pos < 0) { pos = i; continue; }
            return null;
        }
        if (pos < 0) return null;
        var inteiro = p.Substring(0, pos);
        var fracao = p.Substring(pos + 1);
        decimal fator = p[pos] switch { 'H' => 0.001m, 'K' => 1m, 'M' => 1000m, _ => 1000000m };
        var texto = inteiro + "." + (fracao.Length == 0 ? "0" : fracao);
        var valor = decimal.Parse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) * fator;
        return (double)valor;
    }

    private static double ArredondarCasas(double v, int casas)
    {
        try
        {
            return (double)Math.Round((decimal)v, casas, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return Math.Round(v, casas, MidpointRounding.AwayFromZero);
        }
    }

    private static double? Duplex(double? tx, double? rx)
    {
        if (!tx.HasValue || !rx.HasValue) return null;
        try
        {
            var d = (decimal)tx.Value - (decimal)rx.Value;
            return (double)Math.Round(d, 3, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return Math.Round(tx.Value - rx.Value, 3, MidpointRounding.AwayFromZero);
        }
    }

    private static double? PotenciaDbm(double? w)
    {
        if (!w.HasValue || w.Value <= 0) return null;
        var v = 10.0 * Math.Log10(w.Value * 1000.0);
        if (!double.IsFinite(v)) return null;
        return ArredondarCasas(v, 1);
    }

    private static double? AnosLicenciada(DateTime? primeiro)
    {
        if (!primeiro.HasValue) return null;
        int dias = (Hoje - primeiro.Value).Days;
        return (double)Math.Round(dias / 365.25m, 1, MidpointRounding.AwayFromZero);
    }

    private static bool CoordenadaValida(double? lat, double? lon)
    {
        if (!lat.HasValue || !lon.HasValue) return false;
        if (lat.Value == 0 && lon.Value == 0) return false;
        return lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
    }
}

/// <summary>Leitor de CSV com campos entre aspas duplas (aspas escapadas por duplicação) e separador '|'.</summary>
internal sealed class CsvReader
{
    private const char Sep = '|';
    private readonly TextReader _r;
    private readonly StringBuilder _sb = new();

    public CsvReader(TextReader r) => _r = r;

    public List<string>? ReadRecord()
    {
        while (true)
        {
            int c = _r.Read();
            if (c < 0) return null;
            if (c == '\n' || c == '\r') continue; // linhas em branco
            var campos = new List<string>(48);
            _sb.Clear();
            ParseCampos(c, campos);
            return campos;
        }
    }

    private void ParseCampos(int primeiro, List<string> campos)
    {
        int c = primeiro;
        bool emAspas = false;
        bool eraAspas = false; // campo iniciou com aspas
        bool inicioCampo = true;

        while (true)
        {
            if (emAspas)
            {
                if (c < 0) { campos.Add(_sb.ToString()); return; }
                if (c == '"')
                {
                    int n = _r.Peek();
                    if (n == '"') { _r.Read(); _sb.Append('"'); }
                    else emAspas = false;
                }
                else _sb.Append((char)c);
            }
            else
            {
                if (c < 0) { campos.Add(_sb.ToString()); return; }
                if (c == Sep) { campos.Add(_sb.ToString()); _sb.Clear(); inicioCampo = true; eraAspas = false; }
                else if (c == '\n') { campos.Add(_sb.ToString()); return; }
                else if (c == '\r') { /* ignora; \n finaliza */ if (_r.Peek() != '\n') { campos.Add(_sb.ToString()); return; } }
                else if (c == '"' && inicioCampo) { emAspas = true; eraAspas = true; inicioCampo = false; }
                else { _sb.Append((char)c); inicioCampo = false; }
            }
            c = _r.Read();
            _ = eraAspas;
        }
    }
}
