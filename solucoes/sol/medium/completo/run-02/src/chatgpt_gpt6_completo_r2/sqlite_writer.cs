using Microsoft.Data.Sqlite;

namespace AnatelEtl;

internal sealed class SqliteWriter : IDisposable
{
    internal static readonly string[] StationColumns =
    [
        "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude",
        "coordenada_valida", "entidade", "operadora"
    ];
    internal static readonly string[] EmissionColumns =
    [
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
        "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
        "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
        "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
        "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
        "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
        "elevacao_valida"
    ];
    private readonly SqliteConnection connection;
    private readonly SqliteCommand station;
    private readonly SqliteCommand emission;
    private readonly SqliteCommand key;
    private SqliteTransaction transaction;

    public SqliteWriter(string path, string ddl, string tempDirectory)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        Execute("PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-65536; " +
                "PRAGMA temp_store=FILE; PRAGMA temp.cache_size=-32768;");
        Execute($"PRAGMA temp_store_directory='{tempDirectory.Replace("'", "''")}';");
        Execute(ddl);
        Execute("CREATE TEMP TABLE chaves_emissao (chave BLOB PRIMARY KEY) WITHOUT ROWID;");
        station = Insert("estacao", StationColumns, " ON CONFLICT(uf,fistel,num_estacao) DO NOTHING");
        emission = Insert("emissao", EmissionColumns);
        key = connection.CreateCommand();
        key.CommandText = "INSERT OR IGNORE INTO temp.chaves_emissao(chave) VALUES ($key);";
        key.Parameters.Add("$key", SqliteType.Blob);
        key.Prepare();
        transaction = connection.BeginTransaction();
        SetTransaction();
    }

    private void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private SqliteCommand Insert(string table, string[] columns, string conflict = "")
    {
        var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({string.Join(',', columns)}) VALUES " +
            $"({string.Join(',', columns.Select((_, i) => "$p" + i))}){conflict};";
        for (int i = 0; i < columns.Length; i++)
            command.Parameters.Add(new SqliteParameter("$p" + i, DBNull.Value));
        command.Prepare();
        return command;
    }

    private void SetTransaction()
    {
        station.Transaction = transaction;
        emission.Transaction = transaction;
        key.Transaction = transaction;
    }

    public void NextBatch()
    {
        transaction.Commit();
        transaction.Dispose();
        transaction = connection.BeginTransaction();
        SetTransaction();
    }

    public bool FirstEmission(EmissionKey emissionKey)
    {
        key.Parameters[0].Value = Transform.EncodeKey(emissionKey);
        return key.ExecuteNonQuery() == 1;
    }

    private static void Write(SqliteCommand command, object?[] values)
    {
        if (values.Length != command.Parameters.Count) throw new InvalidOperationException("Mapeamento incompleto.");
        for (int i = 0; i < values.Length; i++) command.Parameters[i].Value = values[i] ?? DBNull.Value;
        command.ExecuteNonQuery();
    }

    public void Station(object?[] values) => Write(station, values);
    public void Emission(object?[] values) => Write(emission, values);

    public void Finish(long stations, long emissions)
    {
        transaction.Commit();
        Execute("DROP TABLE temp.chaves_emissao;");
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        if (!Equals(command.ExecuteScalar(), "ok")) throw new InvalidDataException("Falha de integridade SQLite.");
        command.CommandText = "PRAGMA foreign_key_check;";
        using (var reader = command.ExecuteReader())
            if (reader.Read()) throw new InvalidDataException("Referência de estação ausente.");
        command.CommandText = "SELECT COUNT(*) FROM estacao;";
        if ((long)command.ExecuteScalar()! != stations) throw new InvalidDataException("Contagem de estações inconsistente.");
        command.CommandText = "SELECT COUNT(*) FROM emissao;";
        if ((long)command.ExecuteScalar()! != emissions) throw new InvalidDataException("Contagem de emissões inconsistente.");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        if ((long)command.ExecuteScalar()! != 2) throw new InvalidDataException("Tabelas inesperadas no banco final.");
    }

    public void Dispose()
    {
        transaction.Dispose();
        station.Dispose(); emission.Dispose(); key.Dispose(); connection.Dispose();
    }
}
