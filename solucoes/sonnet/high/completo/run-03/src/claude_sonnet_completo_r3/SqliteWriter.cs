using Microsoft.Data.Sqlite;

namespace Etl;

/// <summary>Cria o banco a partir do DDL.sql e grava estações e emissões em lote.</summary>
public sealed class SqliteWriter : IDisposable
{
    private readonly SqliteConnection _conn;
    private SqliteTransaction? _tx;
    private readonly SqliteCommand _insStation;
    private readonly SqliteCommand _insEmission;
    private readonly SqliteParameter[] _sp;
    private readonly SqliteParameter[] _ep;

    private static readonly string[] StationCols =
    {
        "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco",
        "latitude", "longitude", "coordenada_valida", "entidade", "operadora",
    };

    private static readonly string[] EmissionCols =
    {
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
        "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
        "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
        "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
        "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
        "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
        "elevacao_valida",
    };

    public SqliteWriter(string dbFile, string ddlFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbFile)!);
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            if (File.Exists(dbFile + suffix)) File.Delete(dbFile + suffix);

        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _conn.Open();

        Exec("PRAGMA journal_mode = OFF;");
        Exec("PRAGMA synchronous = OFF;");
        Exec("PRAGMA locking_mode = EXCLUSIVE;");
        Exec("PRAGMA cache_size = -262144;");
        Exec("PRAGMA temp_store = MEMORY;");

        Exec(File.ReadAllText(ddlFile));   // esquema exatamente como em DDL.sql

        _tx = _conn.BeginTransaction();

        _insStation = Prepare("estacao", StationCols, out _sp);
        _insEmission = Prepare("emissao", EmissionCols, out _ep);
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand Prepare(string table, string[] cols, out SqliteParameter[] ps)
    {
        var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            $"INSERT INTO {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", cols.Select((_, i) => "$p" + i))})";
        ps = new SqliteParameter[cols.Length];
        for (int i = 0; i < cols.Length; i++)
        {
            ps[i] = cmd.CreateParameter();
            ps[i].ParameterName = "$p" + i;
            cmd.Parameters.Add(ps[i]);
        }
        cmd.Prepare();
        return cmd;
    }

    private static object V(string? s) => s ?? (object)DBNull.Value;
    private static object V(double? d) => d.HasValue ? d.Value : DBNull.Value;
    private static object V(bool b) => b ? 1L : 0L;
    private static object V(bool? b) => b.HasValue ? (b.Value ? 1L : 0L) : DBNull.Value;
    private static object V(DateOnly? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd") : DBNull.Value;

    public void Insert(Station s)
    {
        var p = _sp;
        p[0].Value = s.Fistel;
        p[1].Value = s.NumEstacao;
        p[2].Value = s.Uf;
        p[3].Value = V(s.CodMunicipio);
        p[4].Value = V(s.Municipio);
        p[5].Value = V(s.Endereco);
        p[6].Value = V(s.Latitude);
        p[7].Value = V(s.Longitude);
        p[8].Value = V(s.CoordenadaValida);
        p[9].Value = V(s.Entidade);
        p[10].Value = s.Operadora;
        _insStation.ExecuteNonQuery();
    }

    public void Insert(Emission e)
    {
        var p = _ep;
        p[0].Value = e.Uf;
        p[1].Value = e.Fistel;
        p[2].Value = e.NumEstacao;
        p[3].Value = V(e.NumAto);
        p[4].Value = V(e.Tecnologia);
        p[5].Value = e.Geracao;
        p[6].Value = V(e.DesignacaoEmissao);
        p[7].Value = V(e.LarguraBandaKhz);
        p[8].Value = V(e.FreqTxMhz);
        p[9].Value = V(e.FreqRxMhz);
        p[10].Value = V(e.DuplexMhz);
        p[11].Value = V(e.Azimute);
        p[12].Value = V(e.PotenciaW);
        p[13].Value = V(e.GanhoAntena);
        p[14].Value = V(e.AlturaAntenaM);
        p[15].Value = V(e.DataLicenciamento);
        p[16].Value = V(e.DataPrimeiroLicenciamento);
        p[17].Value = V(e.DataValidade);
        p[18].Value = V(e.Vencida);
        p[19].Value = V(e.AnosLicenciada);
        p[20].Value = V(e.Status);
        p[21].Value = V(e.ClasseEstacao);
        p[22].Value = V(e.Polarizacao);
        p[23].Value = V(e.AnguloElevacaoGraus);
        p[24].Value = V(e.AnguloMeiaPotenciaGraus);
        p[25].Value = V(e.FrenteCostaDb);
        p[26].Value = V(e.CodTipoAntena);
        p[27].Value = V(e.CodEquipamentoAntena);
        p[28].Value = V(e.CodEquipamentoTransmissor);
        p[29].Value = V(e.PotenciaDbm);
        p[30].Value = V(e.AzimuteValido);
        p[31].Value = V(e.ElevacaoValida);
        _insEmission.ExecuteNonQuery();
    }

    public void Commit()
    {
        _tx?.Commit();
        _tx?.Dispose();
        _tx = null;
    }

    public void Dispose()
    {
        _insStation.Dispose();
        _insEmission.Dispose();
        _tx?.Dispose();
        _conn.Dispose();
    }
}
