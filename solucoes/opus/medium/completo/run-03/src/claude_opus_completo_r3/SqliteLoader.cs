using Microsoft.Data.Sqlite;

namespace Etl;

/// <summary>Carga no SQLite: esquema via DDL.sql (sem alterações) e inserts preparados em transação.</summary>
public sealed class SqliteLoader : IDisposable
{
    private readonly SqliteConnection _conn;
    private SqliteTransaction _tx;
    private readonly SqliteCommand _insEst;
    private readonly SqliteCommand _insEmi;
    private readonly SqliteParameter[] _pEst;
    private readonly SqliteParameter[] _pEmi;
    private long _pending;

    private static readonly string[] EstCols =
    {
        "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco", "latitude", "longitude",
        "coordenada_valida", "entidade", "operadora",
    };

    private static readonly string[] EmiCols =
    {
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
        "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
        "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
        "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
        "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
        "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
        "elevacao_valida",
    };

    public SqliteLoader(string dbPath, string ddlPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(dbPath)) File.Delete(dbPath);

        var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        _conn = new SqliteConnection(csb.ToString());
        _conn.Open();

        Exec("PRAGMA journal_mode=OFF;");
        Exec("PRAGMA synchronous=OFF;");
        Exec("PRAGMA temp_store=MEMORY;");
        Exec("PRAGMA cache_size=-200000;");

        Exec(File.ReadAllText(ddlPath));

        _tx = _conn.BeginTransaction();
        (_insEst, _pEst) = Prepare("estacao", EstCols);
        (_insEmi, _pEmi) = Prepare("emissao", EmiCols);
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
        var ps = new SqliteParameter[cols.Length];
        for (int i = 0; i < cols.Length; i++)
        {
            ps[i] = cmd.CreateParameter();
            ps[i].ParameterName = "$p" + i;
            cmd.Parameters.Add(ps[i]);
        }
        cmd.Prepare();
        return (cmd, ps);
    }

    private static object V(string? s) => (object?)s ?? DBNull.Value;
    private static object V(double? d) => d is double v ? v : DBNull.Value;
    private static object V(bool b) => b ? 1L : 0L;
    private static object V(bool? b) => b is bool v ? (v ? 1L : 0L) : DBNull.Value;
    private static object V(DateOnly? d) => d is DateOnly v ? v.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : DBNull.Value;

    public void Insert(EstacaoRow e)
    {
        var p = _pEst;
        p[0].Value = e.Fistel; p[1].Value = e.NumEstacao; p[2].Value = e.Uf;
        p[3].Value = V(e.CodMunicipio); p[4].Value = V(e.Municipio); p[5].Value = V(e.Endereco);
        p[6].Value = V(e.Latitude); p[7].Value = V(e.Longitude); p[8].Value = V(e.CoordenadaValida);
        p[9].Value = V(e.Entidade); p[10].Value = e.Operadora;
        _insEst.ExecuteNonQuery();
        Tick();
    }

    public void Insert(EmissaoRow e)
    {
        var p = _pEmi;
        p[0].Value = e.Uf; p[1].Value = e.Fistel; p[2].Value = e.NumEstacao; p[3].Value = V(e.NumAto);
        p[4].Value = V(e.Tecnologia); p[5].Value = e.Geracao; p[6].Value = V(e.DesignacaoEmissao);
        p[7].Value = V(e.LarguraBandaKhz); p[8].Value = V(e.FreqTxMhz); p[9].Value = V(e.FreqRxMhz);
        p[10].Value = V(e.DuplexMhz); p[11].Value = V(e.Azimute); p[12].Value = V(e.PotenciaW);
        p[13].Value = V(e.GanhoAntena); p[14].Value = V(e.AlturaAntenaM); p[15].Value = V(e.DataLicenciamento);
        p[16].Value = V(e.DataPrimeiroLicenciamento); p[17].Value = V(e.DataValidade); p[18].Value = V(e.Vencida);
        p[19].Value = V(e.AnosLicenciada); p[20].Value = V(e.Status); p[21].Value = V(e.ClasseEstacao);
        p[22].Value = V(e.Polarizacao); p[23].Value = V(e.AnguloElevacaoGraus);
        p[24].Value = V(e.AnguloMeiaPotenciaGraus); p[25].Value = V(e.FrenteCostaDb);
        p[26].Value = V(e.CodTipoAntena); p[27].Value = V(e.CodEquipamentoAntena);
        p[28].Value = V(e.CodEquipamentoTransmissor); p[29].Value = V(e.PotenciaDbm);
        p[30].Value = V(e.AzimuteValido); p[31].Value = V(e.ElevacaoValida);
        _insEmi.ExecuteNonQuery();
        Tick();
    }

    private void Tick()
    {
        if (++_pending < 500_000) return;
        _tx.Commit();
        _tx.Dispose();
        _tx = _conn.BeginTransaction();
        _insEst.Transaction = _tx;
        _insEmi.Transaction = _tx;
        _pending = 0;
    }

    public void Complete()
    {
        _tx.Commit();
        _tx.Dispose();
        _tx = _conn.BeginTransaction();
        _insEst.Transaction = _tx;
        _insEmi.Transaction = _tx;
    }

    public void Dispose()
    {
        _insEst.Dispose();
        _insEmi.Dispose();
        _tx.Dispose();
        _conn.Close();
        _conn.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
