using Etl.Transform;
using Microsoft.Data.Sqlite;

namespace Etl.Load;

/// <summary>
/// Grava estações e emissões no SQLite. Mantém em memória o conjunto de estações já
/// gravadas (primeira linha vence) e o conjunto de chaves de emissão (duplicatas são descartadas).
/// </summary>
public sealed class SqliteLoader : IDisposable
{
    private const int BatchSize = 100_000;

    private readonly SqliteConnection _conn;
    private readonly HashSet<(string Uf, string Fistel, string NumEstacao)> _stations = new();
    private readonly HashSet<EmissionKey> _emissionKeys = new();
    private readonly SqliteCommand _stationCmd;
    private readonly SqliteCommand _emissionCmd;
    private SqliteTransaction? _tx;
    private int _pendingInBatch;

    public long StationsInserted { get; private set; }
    public long EmissionsInserted { get; private set; }
    public long DuplicateEmissions { get; private set; }

    public SqliteLoader(string dbPath, string ddlScript)
    {
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        _conn.Open();

        using (var pragma = _conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = MEMORY; PRAGMA synchronous = OFF;";
            pragma.ExecuteNonQuery();
        }

        using (var ddl = _conn.CreateCommand())
        {
            ddl.CommandText = ddlScript;
            ddl.ExecuteNonQuery();
        }

        _stationCmd = Prepare("estacao", Station.Columns);
        _emissionCmd = Prepare("emissao", Emission.Columns);
        BeginBatch();
    }

    public void Add(MappedRow row)
    {
        var s = row.Station;
        if (_stations.Add((s.Uf, s.Fistel, s.NumEstacao)))
        {
            Bind(_stationCmd, s.Values());
            _stationCmd.ExecuteNonQuery();
            StationsInserted++;
        }

        var e = row.Emission;
        if (!_emissionKeys.Add(e.Key))
        {
            DuplicateEmissions++;
            return;
        }

        Bind(_emissionCmd, e.Values());
        _emissionCmd.ExecuteNonQuery();
        EmissionsInserted++;

        if (++_pendingInBatch >= BatchSize)
        {
            CommitBatch();
            BeginBatch();
        }
    }

    public void Commit()
    {
        CommitBatch();
    }

    public void Dispose()
    {
        _tx?.Dispose();
        _stationCmd.Dispose();
        _emissionCmd.Dispose();
        _conn.Dispose();
    }

    private SqliteCommand Prepare(string table, string[] columns)
    {
        var names = string.Join(", ", columns);
        var parameters = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var cmd = _conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} ({names}) VALUES ({parameters})";
        for (var i = 0; i < columns.Length; i++)
        {
            cmd.Parameters.Add(new SqliteParameter { ParameterName = $"@p{i}" });
        }
        cmd.Prepare();
        return cmd;
    }

    private static void Bind(SqliteCommand cmd, object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        }
    }

    private void BeginBatch()
    {
        _tx = _conn.BeginTransaction();
        _stationCmd.Transaction = _tx;
        _emissionCmd.Transaction = _tx;
        _pendingInBatch = 0;
    }

    private void CommitBatch()
    {
        if (_tx is null) return;
        _tx.Commit();
        _tx.Dispose();
        _tx = null;
    }
}
