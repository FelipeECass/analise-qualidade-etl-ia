using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AnatelEtl;

public sealed class Etl
{
    private static readonly DateOnly Hoje = new(2026, 10, 15);

    private readonly string _filesDir;
    private readonly string _ddlPath;
    private readonly string _dbPath;

    private readonly HashSet<string> _estacoes = new(StringComparer.Ordinal);
    private readonly HashSet<(ulong, ulong)> _emissoes = new();

    private long _linhasLidas, _linhasRejeitadas, _estacoesGravadas, _emissoesGravadas, _emissoesDuplicadas;

    private SqliteCommand _insEstacao = null!;
    private SqliteCommand _insEmissao = null!;

    public Etl(string filesDir, string ddlPath, string dbPath)
    {
        _filesDir = filesDir;
        _ddlPath = ddlPath;
        _dbPath = dbPath;
    }

    public void Run()
    {
        var zips = Directory.GetFiles(_filesDir, "*.zip")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (zips.Count == 0) throw new InvalidOperationException($"Nenhum arquivo ZIP encontrado em {_filesDir}");

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            var f = _dbPath + suffix;
            if (File.Exists(f)) File.Delete(f);
        }

        var connStr = new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = false }.ToString();
        using var conn = new SqliteConnection(connStr);
        conn.Open();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF; PRAGMA temp_store = MEMORY;";
            pragma.ExecuteNonQuery();
        }
        using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = File.ReadAllText(_ddlPath, Encoding.UTF8);
            ddl.ExecuteNonQuery();
        }

        PrepareCommands(conn);

        foreach (var zip in zips)
        {
            Console.WriteLine($"Processando {Path.GetFileName(zip)} ...");
            using var tx = conn.BeginTransaction();
            _insEstacao.Transaction = tx;
            _insEmissao.Transaction = tx;
            ProcessZip(zip);
            tx.Commit();
        }

        Console.WriteLine(
            $"Concluído. Linhas lidas: {_linhasLidas}; rejeitadas: {_linhasRejeitadas}; " +
            $"estações: {_estacoesGravadas}; emissões: {_emissoesGravadas}; emissões duplicadas ignoradas: {_emissoesDuplicadas}.");
    }

    private void PrepareCommands(SqliteConnection conn)
    {
        var estCols = new[] { "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude", "coordenada_valida", "entidade", "operadora" };
        _insEstacao = conn.CreateCommand();
        _insEstacao.CommandText = $"INSERT INTO estacao ({string.Join(",", estCols)}) VALUES ({string.Join(",", estCols.Select((_, i) => "$p" + i))})";
        for (int i = 0; i < estCols.Length; i++) _insEstacao.Parameters.Add(new SqliteParameter("$p" + i, null));

        var emiCols = new[]
        {
            "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao", "largura_banda_khz",
            "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w", "ganho_antena", "altura_antena_m",
            "data_licenciamento", "data_primeiro_licenciamento", "data_validade", "vencida", "anos_licenciada",
            "status", "classe_estacao", "polarizacao", "angulo_elevacao_graus", "angulo_meia_potencia_graus",
            "frente_costa_db", "cod_tipo_antena", "cod_equipamento_antena", "cod_equipamento_transmissor",
            "potencia_dbm", "azimute_valido", "elevacao_valida",
        };
        _insEmissao = conn.CreateCommand();
        _insEmissao.CommandText = $"INSERT INTO emissao ({string.Join(",", emiCols)}) VALUES ({string.Join(",", emiCols.Select((_, i) => "$p" + i))})";
        for (int i = 0; i < emiCols.Length; i++) _insEmissao.Parameters.Add(new SqliteParameter("$p" + i, null));
    }

    private void ProcessZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => e.Length > 0 && !e.FullName.EndsWith('/'))
                    ?? throw new InvalidOperationException($"ZIP sem arquivo CSV: {zipPath}");
        using var stream = entry.Open();
        using var text = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        var csv = new CsvReader(text);

        var record = new List<string>(64);
        if (!csv.ReadRecord(record)) return;

        var header = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < record.Count; i++)
        {
            var name = record[i].Trim().TrimStart('\uFEFF').Trim();
            header.TryAdd(name, i);
        }

        var cols = new Columns(header);
        while (csv.ReadRecord(record))
        {
            _linhasLidas++;
            ProcessRow(record, cols);
        }
    }

    private sealed class Columns
    {
        private readonly Dictionary<string, int> _h;
        public Columns(Dictionary<string, int> h) { _h = h; }

        public int Idx(string name)
        {
            if (_h.TryGetValue(name, out var i)) return i;
            foreach (var kv in _h)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return -1;
        }
    }

    // cache de índices por nome (resolvido por arquivo)
    private Columns? _lastCols;
    private readonly Dictionary<string, int> _idx = new();

    private string? Get(List<string> r, Columns cols, string name)
    {
        if (!ReferenceEquals(_lastCols, cols))
        {
            _lastCols = cols;
            _idx.Clear();
        }
        if (!_idx.TryGetValue(name, out var i))
        {
            i = cols.Idx(name);
            _idx[name] = i;
        }
        if (i < 0 || i >= r.Count) return null;
        return Cleaner.Clean(r[i]);
    }

    private void ProcessRow(List<string> r, Columns c)
    {
        var fistel = Get(r, c, "NumFistel");
        var numEstacao = Get(r, c, "NumEstacao");
        var uf = Cleaner.Upper(Get(r, c, "SiglaUf"));
        if (fistel is null || numEstacao is null || uf is null)
        {
            _linhasRejeitadas++;
            return;
        }

        var numAto = Get(r, c, "NumAto");
        var tecnologia = Cleaner.Upper(Get(r, c, "Tecnologia"));
        var designacao = Cleaner.Upper(Get(r, c, "DesignacaoEmissao"));
        var freqTx = Cleaner.ParseNumber(Get(r, c, "FreqTxMHz"));
        var freqRx = Cleaner.ParseNumber(Get(r, c, "FreqRxMHz"));
        var azimute = Cleaner.ParseNumber(Get(r, c, "Azimute"));

        // --- estação ---
        var estKey = string.Concat(uf, "\u0001", fistel, "\u0001", numEstacao);
        if (_estacoes.Add(estKey))
        {
            var entidade = Cleaner.NormalizeText(Get(r, c, "NomeEntidade"));
            var lat = Cleaner.ParseNumber(Get(r, c, "Latitude"));
            var lon = Cleaner.ParseNumber(Get(r, c, "Longitude"));

            var p = _insEstacao.Parameters;
            p[0].Value = Db(fistel);
            p[1].Value = Db(numEstacao);
            p[2].Value = Db(uf);
            p[3].Value = Db(Get(r, c, "CodMunicipio"));
            p[4].Value = Db(Get(r, c, "Municipio.NomeMunicipio"));
            p[5].Value = Db(Cleaner.NormalizeText(Get(r, c, "EnderecoEstacao")));
            p[6].Value = Db(lat);
            p[7].Value = Db(lon);
            p[8].Value = Cleaner.CoordenadaValida(lat, lon) ? 1L : 0L;
            p[9].Value = Db(entidade);
            p[10].Value = Cleaner.Operadora(entidade);
            _insEstacao.ExecuteNonQuery();
            _estacoesGravadas++;
        }

        // --- emissão (deduplicação) ---
        var key = EmissaoKey(uf, fistel, numEstacao, numAto, azimute, freqTx, freqRx, designacao, tecnologia);
        if (!_emissoes.Add(key))
        {
            _emissoesDuplicadas++;
            return;
        }

        var potW = Cleaner.ParseNumber(Get(r, c, "PotenciaTransmissorWatts"));
        var elev = Cleaner.ParseNumber(Get(r, c, "AnguloElevacao"));
        var dataVal = Cleaner.ParseDate(Get(r, c, "DataValidade"));
        var dataPrim = Cleaner.ParseDate(Get(r, c, "DataPrimeiroLicenciamento"));
        var dataLic = Cleaner.ParseDate(Get(r, c, "DataLicenciamento"));

        var q = _insEmissao.Parameters;
        q[0].Value = Db(uf);
        q[1].Value = Db(fistel);
        q[2].Value = Db(numEstacao);
        q[3].Value = Db(numAto);
        q[4].Value = Db(tecnologia);
        q[5].Value = Cleaner.Geracao(tecnologia);
        q[6].Value = Db(designacao);
        q[7].Value = Db(Cleaner.LarguraBandaKhz(designacao));
        q[8].Value = Db(freqTx);
        q[9].Value = Db(freqRx);
        q[10].Value = Db(Cleaner.DuplexMhz(freqTx, freqRx));
        q[11].Value = Db(azimute);
        q[12].Value = Db(potW);
        q[13].Value = Db(Cleaner.ParseNumber(Get(r, c, "GanhoAntena")));
        q[14].Value = Db(Cleaner.ParseNumber(Get(r, c, "AlturaAntena")));
        q[15].Value = Db(dataLic);
        q[16].Value = Db(dataPrim);
        q[17].Value = Db(dataVal);
        q[18] .Value = dataVal is not null && dataVal.Value < Hoje ? 1L : 0L;
        q[19].Value = Db(Cleaner.AnosLicenciada(dataPrim, Hoje));
        q[20].Value = Db(Get(r, c, "Status.state"));
        q[21].Value = Db(Get(r, c, "CodTipoClasseEstacao"));
        q[22].Value = Db(Cleaner.Upper(Get(r, c, "Polarizacao")));
        q[23].Value = Db(elev);
        q[24].Value = Db(Cleaner.ParseNumber(Get(r, c, "AnguloMeiaPotenciaAntena")));
        q[25].Value = Db(Cleaner.ParseNumber(Get(r, c, "FrenteCostaAntena")));
        q[26].Value = Db(Get(r, c, "CodTipoAntena"));
        q[27].Value = Db(Get(r, c, "CodEquipamentoAntena"));
        q[28].Value = Db(Get(r, c, "CodEquipamentoTransmissor"));
        q[29].Value = Db(Cleaner.PotenciaDbm(potW));
        q[30].Value = azimute is null ? DBNull.Value : (azimute.Value >= 0 && azimute.Value <= 360 ? 1L : 0L);
        q[31].Value = elev is null ? DBNull.Value : (elev.Value >= -90 && elev.Value <= 90 ? 1L : 0L);
        _insEmissao.ExecuteNonQuery();
        _emissoesGravadas++;
    }

    private static object Db(string? s) => s is null ? DBNull.Value : s;
    private static object Db(double? d) => d is null ? DBNull.Value : d.Value;
    private static object Db(DateOnly? d) => d is null ? DBNull.Value : d.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static (ulong, ulong) EmissaoKey(string uf, string fistel, string numEstacao, string? numAto,
        double? azimute, double? tx, double? rx, string? designacao, string? tecnologia)
    {
        var sb = new StringBuilder(128);
        AppendS(sb, uf); AppendS(sb, fistel); AppendS(sb, numEstacao); AppendS(sb, numAto);
        AppendD(sb, azimute); AppendD(sb, tx); AppendD(sb, rx);
        AppendS(sb, designacao); AppendS(sb, tecnologia);

        Span<byte> hash = stackalloc byte[16];
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        MD5.HashData(bytes, hash);
        return (BitConverter.ToUInt64(hash[..8]), BitConverter.ToUInt64(hash[8..]));
    }

    private static void AppendS(StringBuilder sb, string? s)
    {
        if (s is null) sb.Append('\u0002');
        else sb.Append('\u0003').Append(s);
        sb.Append('\u0001');
    }

    private static void AppendD(StringBuilder sb, double? d)
    {
        if (d is null) sb.Append('\u0002');
        else sb.Append('\u0003').Append(BitConverter.DoubleToInt64Bits(d.Value));
        sb.Append('\u0001');
    }
}
