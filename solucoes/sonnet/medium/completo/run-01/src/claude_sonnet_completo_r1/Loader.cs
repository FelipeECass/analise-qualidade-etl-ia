using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Etl;

public sealed class Loader
{
    private static readonly string[] Needed =
    {
        "NumFistel","NumEstacao","NumAto","SiglaUf","CodMunicipio","Municipio.NomeMunicipio","NomeEntidade","EnderecoEstacao",
        "Latitude","Longitude","Tecnologia","DesignacaoEmissao","FreqTxMHz","FreqRxMHz","Azimute","PotenciaTransmissorWatts",
        "GanhoAntena","AlturaAntena","AnguloElevacao","AnguloMeiaPotenciaAntena","FrenteCostaAntena","Polarizacao",
        "CodTipoClasseEstacao","CodTipoAntena","CodEquipamentoAntena","CodEquipamentoTransmissor","Status.state",
        "DataLicenciamento","DataPrimeiroLicenciamento","DataValidade",
    };

    private readonly HashSet<string> _stations = new();
    private readonly HashSet<(ulong, ulong)> _emissions = new();
    private SqliteCommand _insEstacao = null!, _insEmissao = null!;
    private readonly SqliteParameter[] _pe = new SqliteParameter[11];
    private readonly SqliteParameter[] _pm = new SqliteParameter[33];
    public long Rows, Rejected, Stations, Emissions;

    public void Run(string zipDir, string ddlPath, string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        foreach (var f in new[] { dbPath, dbPath + "-journal", dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);

        var zips = Directory.GetFiles(zipDir, "*.zip").OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).ToArray();
        if (zips.Length == 0) throw new InvalidOperationException($"Nenhum ZIP em {zipDir}");

        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        conn.Open();
        Exec(conn, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-200000;");
        Exec(conn, File.ReadAllText(ddlPath, Encoding.UTF8));
        Prepare(conn);

        using var tx = conn.BeginTransaction();
        _insEstacao.Transaction = tx;
        _insEmissao.Transaction = tx;
        foreach (var z in zips)
        {
            Console.WriteLine($"Processando {Path.GetFileName(z)}...");
            ProcessZip(z);
        }
        tx.Commit();
        Console.WriteLine($"Linhas lidas: {Rows}; rejeitadas: {Rejected}; estacoes: {Stations}; emissoes: {Emissions}");
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Prepare(SqliteConnection conn)
    {
        string[] ec = { "fistel","num_estacao","uf","cod_municipio","municipio","endereco","latitude","longitude","coordenada_valida","entidade","operadora" };
        _insEstacao = conn.CreateCommand();
        _insEstacao.CommandText = $"INSERT INTO estacao ({string.Join(",", ec)}) VALUES ({string.Join(",", ec.Select((_, i) => "$e" + i))})";
        for (int i = 0; i < ec.Length; i++) { _pe[i] = _insEstacao.Parameters.AddWithValue("$e" + i, DBNull.Value); }

        string[] mc =
        {
            "uf","fistel","num_estacao","num_ato","tecnologia","geracao","designacao_emissao","largura_banda_khz","freq_tx_mhz","freq_rx_mhz",
            "duplex_mhz","azimute","potencia_w","ganho_antena","altura_antena_m","data_licenciamento","data_primeiro_licenciamento",
            "data_validade","vencida","anos_licenciada","status","classe_estacao","polarizacao","angulo_elevacao_graus",
            "angulo_meia_potencia_graus","frente_costa_db","cod_tipo_antena","cod_equipamento_antena","cod_equipamento_transmissor",
            "potencia_dbm","azimute_valido","elevacao_valida",
        };
        _insEmissao = conn.CreateCommand();
        _insEmissao.CommandText = $"INSERT INTO emissao ({string.Join(",", mc)}) VALUES ({string.Join(",", mc.Select((_, i) => "$m" + i))})";
        for (int i = 0; i < mc.Length; i++) _pm[i] = _insEmissao.Parameters.AddWithValue("$m" + i, DBNull.Value);
        _insEstacao.Prepare();
        _insEmissao.Prepare();
    }

    private void ProcessZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ?? zip.Entries.FirstOrDefault(e => e.Length > 0)
                    ?? throw new InvalidOperationException($"ZIP sem arquivo: {path}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 20);
        var csv = new CsvReader(reader);
        var fields = new List<string>(64);
        if (!csv.Read(fields)) return;

        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < fields.Count; i++)
            idx.TryAdd(fields[i].Trim().TrimStart('\uFEFF'), i);
        var ci = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in idx) ci.TryAdd(kv.Key, kv.Value);
        var col = new Dictionary<string, int>();
        foreach (var n in Needed)
        {
            if (idx.TryGetValue(n, out var k) || ci.TryGetValue(n, out k)) col[n] = k;
            else col[n] = -1;
        }
        foreach (var n in new[] { "NumFistel", "NumEstacao", "SiglaUf" })
            if (col[n] < 0) throw new InvalidOperationException($"Coluna {n} ausente em {path}");

        string? G(string n)
        {
            int k = col[n];
            return k >= 0 && k < fields.Count ? fields[k] : null;
        }

        var keyBuf = new StringBuilder();
        while (csv.Read(fields))
        {
            if (fields.Count == 0) continue;
            Rows++;
            var fistel = Cleaner.Text(G("NumFistel"));
            var numEst = Cleaner.Text(G("NumEstacao"));
            var uf = Cleaner.Upper(G("SiglaUf"));
            if (fistel == null || numEst == null || uf == null) { Rejected++; continue; }

            var numAto = Cleaner.Text(G("NumAto"));
            var tecnologia = Cleaner.Upper(G("Tecnologia"));
            var designacao = Cleaner.Upper(G("DesignacaoEmissao"));
            var tx = Cleaner.Number(G("FreqTxMHz"));
            var rx = Cleaner.Number(G("FreqRxMHz"));
            var azimute = Cleaner.Number(G("Azimute"));

            string stKey = string.Concat(uf, "\u0001", fistel, "\u0001", numEst);
            if (_stations.Add(stKey))
            {
                var lat = Cleaner.Number(G("Latitude"));
                var lon = Cleaner.Number(G("Longitude"));
                var entidade = Cleaner.Normalize(G("NomeEntidade"));
                _pe[0].Value = fistel;
                _pe[1].Value = numEst;
                _pe[2].Value = uf;
                _pe[3].Value = Db(Cleaner.Text(G("CodMunicipio")));
                _pe[4].Value = Db(Cleaner.Text(G("Municipio.NomeMunicipio")));
                _pe[5].Value = Db(Cleaner.Normalize(G("EnderecoEstacao")));
                _pe[6].Value = Db(lat);
                _pe[7].Value = Db(lon);
                _pe[8].Value = Derivations.CoordenadaValida(lat, lon) ? 1L : 0L;
                _pe[9].Value = Db(entidade);
                _pe[10].Value = Derivations.Operadora(entidade);
                _insEstacao.ExecuteNonQuery();
                Stations++;
            }

            keyBuf.Clear();
            keyBuf.Append(uf).Append('\u0001').Append(fistel).Append('\u0001').Append(numEst).Append('\u0001')
                  .Append(numAto ?? "\u0002").Append('\u0001').Append(Key(azimute)).Append('\u0001')
                  .Append(Key(tx)).Append('\u0001').Append(Key(rx)).Append('\u0001')
                  .Append(designacao ?? "\u0002").Append('\u0001').Append(tecnologia ?? "\u0002");
            if (!_emissions.Add(Hash(keyBuf))) continue;

            var potencia = Cleaner.Number(G("PotenciaTransmissorWatts"));
            var elev = Cleaner.Number(G("AnguloElevacao"));
            var dLic = Cleaner.Date(G("DataLicenciamento"));
            var dPrim = Cleaner.Date(G("DataPrimeiroLicenciamento"));
            var dVal = Cleaner.Date(G("DataValidade"));

            _pm[0].Value = uf;
            _pm[1].Value = fistel;
            _pm[2].Value = numEst;
            _pm[3].Value = Db(numAto);
            _pm[4].Value = Db(tecnologia);
            _pm[5].Value = Derivations.Geracao(tecnologia);
            _pm[6].Value = Db(designacao);
            _pm[7].Value = Db(Derivations.LarguraBandaKhz(designacao));
            _pm[8].Value = Db(tx);
            _pm[9].Value = Db(rx);
            _pm[10].Value = Db(Derivations.Duplex(tx, rx));
            _pm[11].Value = Db(azimute);
            _pm[12].Value = Db(potencia);
            _pm[13].Value = Db(Cleaner.Number(G("GanhoAntena")));
            _pm[14].Value = Db(Cleaner.Number(G("AlturaAntena")));
            _pm[15].Value = Db(dLic);
            _pm[16].Value = Db(dPrim);
            _pm[17].Value = Db(dVal);
            _pm[18].Value = Derivations.Vencida(dVal) ? 1L : 0L;
            _pm[19].Value = Db(Derivations.AnosLicenciada(dPrim));
            _pm[20].Value = Db(Cleaner.Text(G("Status.state")));
            _pm[21].Value = Db(Cleaner.Text(G("CodTipoClasseEstacao")));
            _pm[22].Value = Db(Cleaner.Upper(G("Polarizacao")));
            _pm[23].Value = Db(elev);
            _pm[24].Value = Db(Cleaner.Number(G("AnguloMeiaPotenciaAntena")));
            _pm[25].Value = Db(Cleaner.Number(G("FrenteCostaAntena")));
            _pm[26].Value = Db(Cleaner.Text(G("CodTipoAntena")));
            _pm[27].Value = Db(Cleaner.Text(G("CodEquipamentoAntena")));
            _pm[28].Value = Db(Cleaner.Text(G("CodEquipamentoTransmissor")));
            _pm[29].Value = Db(Derivations.PotenciaDbm(potencia));
            _pm[30].Value = Db(Derivations.Range(azimute, 0, 360));
            _pm[31].Value = Db(Derivations.Range(elev, -90, 90));
            _insEmissao.ExecuteNonQuery();
            Emissions++;
        }
    }

    private static string Key(double? d) => d == null ? "\u0002" : BitConverter.DoubleToInt64Bits(d.Value == 0 ? 0.0 : d.Value).ToString();

    private static (ulong, ulong) Hash(StringBuilder sb)
    {
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var h = XxHash128.HashToUInt128(bytes);
        return ((ulong)(h >> 64), (ulong)h);
    }

    private static object Db(string? s) => s is null ? DBNull.Value : s;
    private static object Db(double? d) => d is null ? DBNull.Value : d.Value;
    private static object Db(bool? b) => b is null ? DBNull.Value : (b.Value ? 1L : 0L);
    private static object Db(DateTime? d) => d is null ? DBNull.Value : d.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
