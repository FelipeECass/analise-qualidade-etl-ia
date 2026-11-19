using Microsoft.Data.Sqlite;
using System.Text;

namespace AnatelEtl;

internal sealed class DatabaseWriter : IDisposable
{
    internal static readonly string[] StationColumns =
    ["fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude", "coordenada_valida", "entidade", "operadora"];
    internal static readonly string[] EmissionColumns =
    [
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao", "largura_banda_khz",
        "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w", "ganho_antena", "altura_antena_m",
        "data_licenciamento", "data_primeiro_licenciamento", "data_validade", "vencida", "anos_licenciada",
        "status", "classe_estacao", "polarizacao", "angulo_elevacao_graus", "angulo_meia_potencia_graus",
        "frente_costa_db", "cod_tipo_antena", "cod_equipamento_antena", "cod_equipamento_transmissor",
        "potencia_dbm", "azimute_valido", "elevacao_valida"
    ];
    private readonly SqliteConnection connection;
    private readonly SqliteCommand station;
    private readonly SqliteCommand emission;
    private readonly SqliteCommand seen;
    private readonly MemoryStream keyBuffer = new(256);
    private readonly BinaryWriter keyWriter;
    private SqliteTransaction? transaction;
    public long Stations { get; private set; }
    public long Emissions { get; private set; }

    public DatabaseWriter(string output, string ddl)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = output, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        connection.Open();
        string temporary = Path.Combine(Path.GetDirectoryName(output)!, ".temp");
        Directory.CreateDirectory(temporary);
        Execute("PRAGMA temp_store_directory='" + temporary.Replace("'", "''") + "';");
        Execute("PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-65536; PRAGMA temp_store=FILE;");
        Execute(File.ReadAllText(ddl));
        Execute("CREATE TEMP TABLE seen_emission (key BLOB PRIMARY KEY) WITHOUT ROWID;");
        station = PrepareInsert("estacao", StationColumns, " ON CONFLICT(uf,fistel,num_estacao) DO NOTHING");
        emission = PrepareInsert("emissao", EmissionColumns, "");
        seen = connection.CreateCommand();
        seen.CommandText = "INSERT OR IGNORE INTO temp.seen_emission(key) VALUES ($key)";
        seen.Parameters.Add("$key", SqliteType.Blob);
        seen.Prepare();
        keyWriter = new BinaryWriter(keyBuffer, Encoding.UTF8, true);
    }

    private void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private SqliteCommand PrepareInsert(string table, string[] columns, string suffix)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select((_, i) => "$p" + i))}){suffix}";
        for (int i = 0; i < columns.Length; i++) command.Parameters.Add(new SqliteParameter("$p" + i, DBNull.Value));
        command.Prepare();
        return command;
    }
    public void Begin()
    {
        transaction = connection.BeginTransaction();
        station.Transaction = emission.Transaction = seen.Transaction = transaction;
    }
    public void Commit()
    {
        transaction!.Commit();
        transaction.Dispose();
        transaction = null;
        station.Transaction = emission.Transaction = seen.Transaction = null;
    }
    // Length-prefixed text, type tags and canonical REAL values prevent ambiguous keys.
    public bool FirstEmission(string uf, string fistel, string number, string? act,
        double? azimuth, double? tx, double? rx, string? designation, string? technology)
    {
        keyBuffer.SetLength(0);
        WriteText(uf); WriteText(fistel); WriteText(number); WriteText(act);
        WriteNumber(azimuth); WriteNumber(tx); WriteNumber(rx);
        WriteText(designation); WriteText(technology);
        keyWriter.Flush();
        seen.Parameters[0].Value = keyBuffer.ToArray();
        return seen.ExecuteNonQuery() == 1;
    }
    private void WriteText(string? text)
    {
        keyWriter.Write((byte)(text is null ? 0 : 1));
        if (text is not null) keyWriter.Write(text);
    }
    private void WriteNumber(double? number)
    {
        keyWriter.Write((byte)(number is null ? 0 : 2));
        if (number is { } n) keyWriter.Write(n == 0 ? 0.0 : n);
    }
    private static int Insert(SqliteCommand command, object?[] values)
    {
        if (values.Length != command.Parameters.Count) throw new InvalidOperationException("Mapeamento SQL inconsistente.");
        for (int i = 0; i < values.Length; i++) command.Parameters[i].Value = values[i] ?? DBNull.Value;
        return command.ExecuteNonQuery();
    }
    public void Station(object?[] values) => Stations += Insert(station, values);
    public void Emission(object?[] values) => Emissions += Insert(emission, values);
    public void Check()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check";
        if (!Equals(command.ExecuteScalar(), "ok")) throw new InvalidDataException("SQLite quick_check falhou.");
        command.CommandText = "PRAGMA foreign_key_check";
        using var reader = command.ExecuteReader();
        if (reader.Read()) throw new InvalidDataException("SQLite: violação de chave estrangeira.");
    }
    public void Dispose()
    {
        transaction?.Dispose();
        station.Dispose(); emission.Dispose(); seen.Dispose();
        keyWriter.Dispose(); keyBuffer.Dispose(); connection.Dispose();
    }
}
