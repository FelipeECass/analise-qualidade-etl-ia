using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AnatelEtl;

public sealed class Stats
{
    public long Read, Rejected, DuplicateEmissions, Stations, Emissions;
}

/// <summary>Localiza as pastas de entrada/saída (D:\Projects\TCC ou, fora do Windows/D:, a raiz que contém files e src\DDL.sql).</summary>
public static class PathResolver
{
    public static string FindRoot()
    {
        var fixedRoot = @"D:\Projects\TCC";
        if (Directory.Exists(Path.Combine(fixedRoot, "files"))) return fixedRoot;

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "files")) &&
                    File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException("Não foi possível localizar a pasta raiz (D:\\Projects\\TCC) com 'files' e 'src\\DDL.sql'.");
    }
}

/// <summary>Índices das colunas do cabeçalho, localizadas por nome.</summary>
public sealed class Columns
{
    public int Status, NomeEntidade, NumFistel, NumAto, NumEstacao, Endereco, SiglaUf, CodMunicipio, Designacao, Tecnologia,
        FreqTx, FreqRx, Azimute, ClasseEstacao, CodTipoAntena, CodEqAntena, Ganho, FrenteCosta, MeiaPotencia, Elevacao,
        Polarizacao, Altura, CodEqTransmissor, Potencia, Latitude, Longitude, DataLic, DataPrimeiro, DataValidade, Municipio;

    public Columns(CsvReader header)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.FieldCount; i++)
        {
            var name = new string(header.Field(i)).Trim().TrimStart('\uFEFF');
            map.TryAdd(name, i);
        }
        int Req(string n) => map.TryGetValue(n, out var ix) ? ix : throw new InvalidDataException($"Coluna obrigatória ausente no cabeçalho: {n}");
        Status = Req("Status.state"); NomeEntidade = Req("NomeEntidade"); NumFistel = Req("NumFistel"); NumAto = Req("NumAto");
        NumEstacao = Req("NumEstacao"); Endereco = Req("EnderecoEstacao"); SiglaUf = Req("SiglaUf"); CodMunicipio = Req("CodMunicipio");
        Designacao = Req("DesignacaoEmissao"); Tecnologia = Req("Tecnologia"); FreqTx = Req("FreqTxMHz"); FreqRx = Req("FreqRxMHz");
        Azimute = Req("Azimute"); ClasseEstacao = Req("CodTipoClasseEstacao"); CodTipoAntena = Req("CodTipoAntena");
        CodEqAntena = Req("CodEquipamentoAntena"); Ganho = Req("GanhoAntena"); FrenteCosta = Req("FrenteCostaAntena");
        MeiaPotencia = Req("AnguloMeiaPotenciaAntena"); Elevacao = Req("AnguloElevacao"); Polarizacao = Req("Polarizacao");
        Altura = Req("AlturaAntena"); CodEqTransmissor = Req("CodEquipamentoTransmissor"); Potencia = Req("PotenciaTransmissorWatts");
        Latitude = Req("Latitude"); Longitude = Req("Longitude"); DataLic = Req("DataLicenciamento");
        DataPrimeiro = Req("DataPrimeiroLicenciamento"); DataValidade = Req("DataValidade"); Municipio = Req("Municipio.NomeMunicipio");
    }
}

/// <summary>Carga: cria o banco a partir do DDL, lê os ZIPs, transforma, deduplica e grava.</summary>
public sealed class Loader : IDisposable
{
    private readonly SqliteConnection _conn;
    private SqliteTransaction _tx;
    private readonly SqliteCommand _insEstacao, _insEmissao;
    private readonly SqliteParameter[] _pe = new SqliteParameter[11];
    private readonly SqliteParameter[] _pm = new SqliteParameter[32];

    private readonly HashSet<(ulong, ulong)> _stations = new();
    private readonly HashSet<(ulong, ulong)> _emissions = new();
    private byte[] _keyBuf = new byte[1024];
    private readonly byte[] _hash = new byte[16];

    public Stats Stats { get; } = new();

    public Loader(string dbPath, string ddlPath)
    {
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            var f = dbPath + suffix;
            if (File.Exists(f)) File.Delete(f);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _conn = new SqliteConnection($"Data Source={dbPath};Foreign Keys=False;Pooling=False");
        _conn.Open();
        Exec("PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-400000; PRAGMA locking_mode=EXCLUSIVE;");
        Exec(File.ReadAllText(ddlPath, Encoding.UTF8)); // esquema do DDL.sql, sem alterações

        _tx = _conn.BeginTransaction();
        _insEstacao = _conn.CreateCommand();
        _insEstacao.Transaction = _tx;
        _insEstacao.CommandText =
            "INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) " +
            "VALUES ($a0,$a1,$a2,$a3,$a4,$a5,$a6,$a7,$a8,$a9,$a10)";
        for (int i = 0; i < _pe.Length; i++) { _pe[i] = _insEstacao.CreateParameter(); _pe[i].ParameterName = "$a" + i; _insEstacao.Parameters.Add(_pe[i]); }
        _insEstacao.Prepare();

        _insEmissao = _conn.CreateCommand();
        _insEmissao.Transaction = _tx;
        _insEmissao.CommandText =
            "INSERT INTO emissao (uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz," +
            "duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida," +
            "anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena," +
            "cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida) VALUES (" +
            string.Join(",", Enumerable.Range(0, 32).Select(i => "$m" + i)) + ")";
        for (int i = 0; i < _pm.Length; i++) { _pm[i] = _insEmissao.CreateParameter(); _pm[i].ParameterName = "$m" + i; _insEmissao.Parameters.Add(_pm[i]); }
        _insEmissao.Prepare();
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object V(string? s) => s is null ? DBNull.Value : s;
    private static object V(double? d) => d.HasValue ? d.Value : DBNull.Value;
    private static object V(DateOnly? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : DBNull.Value;
    private static object V(bool b) => b ? 1L : 0L;
    private static object V(bool? b) => b.HasValue ? (b.Value ? 1L : 0L) : DBNull.Value;

    // ---- chaves de deduplicação (hash MD5 de 128 bits sobre uma codificação sem ambiguidade) ----
    private int _klen;
    private void KReset() => _klen = 0;
    private void KEnsure(int extra) { if (_klen + extra > _keyBuf.Length) Array.Resize(ref _keyBuf, Math.Max(_keyBuf.Length * 2, _klen + extra)); }
    private void KStr(string? s)
    {
        if (s is null) { KEnsure(1); _keyBuf[_klen++] = 0; return; }
        int bytes = s.Length * 2;
        KEnsure(5 + bytes);
        _keyBuf[_klen++] = 1;
        BitConverter.TryWriteBytes(_keyBuf.AsSpan(_klen), s.Length); _klen += 4;
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(s.AsSpan()).CopyTo(_keyBuf.AsSpan(_klen)); _klen += bytes;
    }
    private void KNum(double? d)
    {
        KEnsure(9);
        if (!d.HasValue) { _keyBuf[_klen++] = 0; return; }
        _keyBuf[_klen++] = 1;
        BitConverter.TryWriteBytes(_keyBuf.AsSpan(_klen), d.Value); _klen += 8;
    }
    private (ulong, ulong) KHash()
    {
        MD5.HashData(_keyBuf.AsSpan(0, _klen), _hash);
        return (BitConverter.ToUInt64(_hash, 0), BitConverter.ToUInt64(_hash, 8));
    }

    public void ProcessZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name))
                        .OrderByDescending(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault() ?? throw new InvalidDataException($"ZIP sem arquivo: {zipPath}");
        using var stream = entry.Open();
        using var csv = new CsvReader(stream);
        if (!csv.Read()) throw new InvalidDataException($"Arquivo vazio em {zipPath}");
        var c = new Columns(csv);

        while (csv.Read())
        {
            Stats.Read++;
            string? fistel = Cleaner.Text(csv.Field(c.NumFistel));
            string? numEstacao = Cleaner.Text(csv.Field(c.NumEstacao));
            string? uf = Cleaner.Upper(csv.Field(c.SiglaUf));
            if (fistel is null || numEstacao is null || uf is null) { Stats.Rejected++; continue; }

            // estação (primeira ocorrência define os atributos)
            KReset(); KStr(uf); KStr(fistel); KStr(numEstacao);
            if (_stations.Add(KHash()))
            {
                string? entidade = Cleaner.Normalize(csv.Field(c.NomeEntidade));
                double? lat = Cleaner.Number(csv.Field(c.Latitude));
                double? lon = Cleaner.Number(csv.Field(c.Longitude));
                _pe[0].Value = fistel;
                _pe[1].Value = numEstacao;
                _pe[2].Value = uf;
                _pe[3].Value = V(Cleaner.Text(csv.Field(c.CodMunicipio)));
                _pe[4].Value = V(Cleaner.Text(csv.Field(c.Municipio)));
                _pe[5].Value = V(Cleaner.Normalize(csv.Field(c.Endereco)));
                _pe[6].Value = V(lat);
                _pe[7].Value = V(lon);
                _pe[8].Value = V(Derivations.CoordenadaValida(lat, lon));
                _pe[9].Value = V(entidade);
                _pe[10].Value = Derivations.Operadora(entidade);
                _insEstacao.ExecuteNonQuery();
                Stats.Stations++;
            }

            string? numAto = Cleaner.Text(csv.Field(c.NumAto));
            string? tecnologia = Cleaner.Upper(csv.Field(c.Tecnologia));
            string? designacao = Cleaner.Upper(csv.Field(c.Designacao));
            double? tx = Cleaner.Number(csv.Field(c.FreqTx));
            double? rx = Cleaner.Number(csv.Field(c.FreqRx));
            double? azimute = Cleaner.Number(csv.Field(c.Azimute));

            KReset(); KStr(uf); KStr(fistel); KStr(numEstacao); KStr(numAto); KNum(azimute); KNum(tx); KNum(rx); KStr(designacao); KStr(tecnologia);
            if (!_emissions.Add(KHash())) { Stats.DuplicateEmissions++; continue; }

            double? potencia = Cleaner.Number(csv.Field(c.Potencia));
            DateOnly? dLic = Cleaner.Date(csv.Field(c.DataLic));
            DateOnly? dPrim = Cleaner.Date(csv.Field(c.DataPrimeiro));
            DateOnly? dVal = Cleaner.Date(csv.Field(c.DataValidade));
            double? elevacao = Cleaner.Number(csv.Field(c.Elevacao));

            _pm[0].Value = uf;
            _pm[1].Value = fistel;
            _pm[2].Value = numEstacao;
            _pm[3].Value = V(numAto);
            _pm[4].Value = V(tecnologia);
            _pm[5].Value = Derivations.Geracao(tecnologia);
            _pm[6].Value = V(designacao);
            _pm[7].Value = V(Derivations.LarguraBandaKhz(designacao));
            _pm[8].Value = V(tx);
            _pm[9].Value = V(rx);
            _pm[10].Value = V(Derivations.DuplexMhz(tx, rx));
            _pm[11].Value = V(azimute);
            _pm[12].Value = V(potencia);
            _pm[13].Value = V(Cleaner.Number(csv.Field(c.Ganho)));
            _pm[14].Value = V(Cleaner.Number(csv.Field(c.Altura)));
            _pm[15].Value = V(dLic);
            _pm[16].Value = V(dPrim);
            _pm[17].Value = V(dVal);
            _pm[18].Value = V(Derivations.Vencida(dVal));
            _pm[19].Value = V(Derivations.AnosLicenciada(dPrim));
            _pm[20].Value = V(Cleaner.Text(csv.Field(c.Status)));
            _pm[21].Value = V(Cleaner.Text(csv.Field(c.ClasseEstacao)));
            _pm[22].Value = V(Cleaner.Upper(csv.Field(c.Polarizacao)));
            _pm[23].Value = V(elevacao);
            _pm[24].Value = V(Cleaner.Number(csv.Field(c.MeiaPotencia)));
            _pm[25].Value = V(Cleaner.Number(csv.Field(c.FrenteCosta)));
            _pm[26].Value = V(Cleaner.Text(csv.Field(c.CodTipoAntena)));
            _pm[27].Value = V(Cleaner.Text(csv.Field(c.CodEqAntena)));
            _pm[28].Value = V(Cleaner.Text(csv.Field(c.CodEqTransmissor)));
            _pm[29].Value = V(Derivations.PotenciaDbm(potencia));
            _pm[30].Value = V(Derivations.AzimuteValido(azimute));
            _pm[31].Value = V(Derivations.ElevacaoValida(elevacao));
            _insEmissao.ExecuteNonQuery();
            Stats.Emissions++;
        }
    }

    public void Commit()
    {
        _tx.Commit();
    }

    public void Dispose()
    {
        _insEstacao.Dispose();
        _insEmissao.Dispose();
        _tx.Dispose();
        _conn.Dispose();
    }
}
