using Microsoft.Data.Sqlite;

namespace AnatelEtl;

/// <summary>Cria o banco a partir do DDL e grava estações e emissões.</summary>
public sealed class SqliteLoader : IDisposable
{
    private const int BatchSize = 100_000;
    private readonly SqliteConnection _conn;
    private SqliteTransaction _tx;
    private readonly SqliteCommand _insEstacao, _insEmissao;
    private readonly SqliteParameter[] _pEstacao, _pEmissao;
    private int _pending;

    public SqliteLoader(string dbPath, string ddl)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        if (File.Exists(dbPath)) File.Delete(dbPath);

        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false,
        }.ToString());
        _conn.Open();
        Exec("PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-200000;");
        Exec(ddl);

        _tx = _conn.BeginTransaction();
        (_insEstacao, _pEstacao) = Prepare("estacao", new[]
        {
            "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude",
            "coordenada_valida", "entidade", "operadora",
        });
        (_insEmissao, _pEmissao) = Prepare("emissao", new[]
        {
            "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
            "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
            "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento", "data_validade",
            "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao", "angulo_elevacao_graus",
            "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena", "cod_equipamento_antena",
            "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido", "elevacao_valida",
        });
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private (SqliteCommand, SqliteParameter[]) Prepare(string table, string[] cols)
    {
        var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", cols.Select((_, i) => "$p" + i))})";
        var ps = cols.Select((_, i) => cmd.Parameters.Add(new SqliteParameter("$p" + i, null))).ToArray();
        cmd.Prepare();
        return (cmd, ps);
    }

    private static object V(string? s) => (object?)s ?? DBNull.Value;
    private static object V(double? d) => d.HasValue ? d.Value : DBNull.Value;
    private static object V(bool b) => b ? 1L : 0L;
    private static object V(bool? b) => b.HasValue ? (b.Value ? 1L : 0L) : DBNull.Value;
    private static object V(DateOnly? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : DBNull.Value;

    public void Insert(Estacao e)
    {
        var p = _pEstacao;
        p[0].Value = e.Fistel; p[1].Value = e.NumEstacao; p[2].Value = e.Uf; p[3].Value = V(e.CodMunicipio);
        p[4].Value = V(e.Municipio); p[5].Value = V(e.Endereco); p[6].Value = V(e.Latitude); p[7].Value = V(e.Longitude);
        p[8].Value = V(e.CoordenadaValida); p[9].Value = V(e.Entidade); p[10].Value = e.Operadora;
        _insEstacao.ExecuteNonQuery();
        Tick();
    }

    public void Insert(Emissao e)
    {
        var p = _pEmissao;
        p[0].Value = e.Uf; p[1].Value = e.Fistel; p[2].Value = e.NumEstacao; p[3].Value = V(e.NumAto);
        p[4].Value = V(e.Tecnologia); p[5].Value = e.Geracao; p[6].Value = V(e.DesignacaoEmissao);
        p[7].Value = V(e.LarguraBandaKhz); p[8].Value = V(e.FreqTxMhz); p[9].Value = V(e.FreqRxMhz);
        p[10].Value = V(e.DuplexMhz); p[11].Value = V(e.Azimute); p[12].Value = V(e.PotenciaW);
        p[13].Value = V(e.GanhoAntena); p[14].Value = V(e.AlturaAntenaM); p[15].Value = V(e.DataLicenciamento);
        p[16].Value = V(e.DataPrimeiroLicenciamento); p[17].Value = V(e.DataValidade); p[18].Value = V(e.Vencida);
        p[19].Value = V(e.AnosLicenciada); p[20].Value = V(e.Status); p[21].Value = V(e.ClasseEstacao);
        p[22].Value = V(e.Polarizacao); p[23].Value = V(e.AnguloElevacaoGraus); p[24].Value = V(e.AnguloMeiaPotenciaGraus);
        p[25].Value = V(e.FrenteCostaDb); p[26].Value = V(e.CodTipoAntena); p[27].Value = V(e.CodEquipamentoAntena);
        p[28].Value = V(e.CodEquipamentoTransmissor); p[29].Value = V(e.PotenciaDbm); p[30].Value = V(e.AzimuteValido);
        p[31].Value = V(e.ElevacaoValida);
        _insEmissao.ExecuteNonQuery();
        Tick();
    }

    private void Tick()
    {
        if (++_pending < BatchSize) return;
        _tx.Commit();
        _tx.Dispose();
        _tx = _conn.BeginTransaction();
        _insEstacao.Transaction = _tx;
        _insEmissao.Transaction = _tx;
        _pending = 0;
    }

    public void Complete()
    {
        _tx.Commit();
        _tx.Dispose();
        _tx = _conn.BeginTransaction();
        _insEstacao.Transaction = _tx;
        _insEmissao.Transaction = _tx;
    }

    public void Dispose()
    {
        _insEstacao.Dispose();
        _insEmissao.Dispose();
        _tx.Dispose();
        _conn.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
