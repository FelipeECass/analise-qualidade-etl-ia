using Microsoft.Data.Sqlite;

sealed class Database : IDisposable
{
    readonly SqliteConnection _conn;
    SqliteTransaction? _tx;
    readonly SqliteCommand _insEstacao, _insEmissao;

    static readonly string[] EmissaoCols =
    {
        "uf","fistel","num_estacao","num_ato","tecnologia","geracao","designacao_emissao","largura_banda_khz",
        "freq_tx_mhz","freq_rx_mhz","duplex_mhz","azimute","potencia_w","ganho_antena","altura_antena_m",
        "data_licenciamento","data_primeiro_licenciamento","data_validade","vencida","anos_licenciada",
        "status","classe_estacao","polarizacao","angulo_elevacao_graus","angulo_meia_potencia_graus",
        "frente_costa_db","cod_tipo_antena","cod_equipamento_antena","cod_equipamento_transmissor",
        "potencia_dbm","azimute_valido","elevacao_valida"
    };
    static readonly string[] EstacaoCols =
    {
        "fistel","num_estacao","uf","cod_municipio","municipio","endereco","latitude","longitude",
        "coordenada_valida","entidade","operadora"
    };

    public Database(string dbPath, string ddlPath)
    {
        foreach (var f in new[] { dbPath, dbPath + "-journal", dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _conn.Open();
        using (var p = _conn.CreateCommand())
        {
            p.CommandText = "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-200000;";
            p.ExecuteNonQuery();
        }
        using (var d = _conn.CreateCommand())
        {
            d.CommandText = File.ReadAllText(ddlPath);
            d.ExecuteNonQuery();
        }
        _tx = _conn.BeginTransaction();
        _insEstacao = Prepare("estacao", EstacaoCols);
        _insEmissao = Prepare("emissao", EmissaoCols);
    }

    SqliteCommand Prepare(string table, string[] cols)
    {
        var c = _conn.CreateCommand();
        c.Transaction = _tx;
        c.CommandText = $"INSERT INTO {table} ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select(x => "$" + x))})";
        foreach (var col in cols) c.Parameters.Add(new SqliteParameter("$" + col, DBNull.Value));
        c.Prepare();
        return c;
    }

    static void Set(SqliteCommand c, int i, object? v) => c.Parameters[i].Value = v ?? DBNull.Value;

    public void InsertEstacao(object?[] values)
    {
        for (int i = 0; i < values.Length; i++) Set(_insEstacao, i, values[i]);
        _insEstacao.ExecuteNonQuery();
    }

    public void InsertEmissao(object?[] values)
    {
        for (int i = 0; i < values.Length; i++) Set(_insEmissao, i, values[i]);
        _insEmissao.ExecuteNonQuery();
    }

    public void Commit() { _tx!.Commit(); _tx = null; }

    public void Dispose()
    {
        _insEstacao.Dispose(); _insEmissao.Dispose();
        _tx?.Dispose();
        _conn.Dispose();
    }
}
