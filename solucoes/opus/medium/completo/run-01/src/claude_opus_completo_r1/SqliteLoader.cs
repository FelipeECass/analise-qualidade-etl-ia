using Microsoft.Data.Sqlite;

namespace AnatelEtl;

/// <summary>Cria o banco a partir do DDL.sql e grava estações/emissões com comandos preparados.</summary>
public sealed class SqliteLoader : IDisposable
{
    private readonly SqliteConnection _conn;
    private SqliteTransaction _tx;
    private readonly SqliteCommand _insEstacao, _insEmissao;
    private readonly SqliteParameter[] _pEst, _pEmi;

    private static readonly string[] EstacaoCols =
    {
        "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco",
        "latitude", "longitude", "coordenada_valida", "entidade", "operadora",
    };

    private static readonly string[] EmissaoCols =
    {
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
        "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
        "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
        "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
        "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
        "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
        "elevacao_valida",
    };

    public SqliteLoader(string dbPath, string ddl)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        _conn = new SqliteConnection(cs);
        _conn.Open();
        Exec("PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-200000;");
        Exec(ddl);

        _tx = _conn.BeginTransaction();
        (_insEstacao, _pEst) = Prepare("estacao", EstacaoCols);
        (_insEmissao, _pEmi) = Prepare("emissao", EmissaoCols);
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
        for (int i = 0; i < cols.Length; i++) ps[i] = cmd.Parameters.Add(new SqliteParameter("$p" + i, null));
        cmd.Prepare();
        return (cmd, ps);
    }

    private static object V(string? s) => s is null ? DBNull.Value : s;
    private static object V(double? d) => d is null ? DBNull.Value : d.Value;
    private static object V(bool b) => b ? 1L : 0L;
    private static object V(bool? b) => b is null ? DBNull.Value : (b.Value ? 1L : 0L);
    private static object V(DateOnly? d) => d is null ? DBNull.Value : d.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public void Insert(Estacao e)
    {
        var p = _pEst;
        p[0].Value = e.Fistel; p[1].Value = e.NumEstacao; p[2].Value = e.Uf;
        p[3].Value = V(e.CodMunicipio); p[4].Value = V(e.Municipio); p[5].Value = V(e.Endereco);
        p[6].Value = V(e.Latitude); p[7].Value = V(e.Longitude); p[8].Value = V(e.CoordenadaValida);
        p[9].Value = V(e.Entidade); p[10].Value = e.Operadora;
        _insEstacao.ExecuteNonQuery();
    }

    public void Insert(Emissao m)
    {
        var p = _pEmi;
        p[0].Value = m.Uf; p[1].Value = m.Fistel; p[2].Value = m.NumEstacao; p[3].Value = V(m.NumAto);
        p[4].Value = V(m.Tecnologia); p[5].Value = m.Geracao; p[6].Value = V(m.DesignacaoEmissao);
        p[7].Value = V(m.LarguraBandaKhz); p[8].Value = V(m.FreqTxMhz); p[9].Value = V(m.FreqRxMhz);
        p[10].Value = V(m.DuplexMhz); p[11].Value = V(m.Azimute); p[12].Value = V(m.PotenciaW);
        p[13].Value = V(m.GanhoAntena); p[14].Value = V(m.AlturaAntenaM); p[15].Value = V(m.DataLicenciamento);
        p[16].Value = V(m.DataPrimeiroLicenciamento); p[17].Value = V(m.DataValidade); p[18].Value = V(m.Vencida);
        p[19].Value = V(m.AnosLicenciada); p[20].Value = V(m.Status); p[21].Value = V(m.ClasseEstacao);
        p[22].Value = V(m.Polarizacao); p[23].Value = V(m.AnguloElevacaoGraus); p[24].Value = V(m.AnguloMeiaPotenciaGraus);
        p[25].Value = V(m.FrenteCostaDb); p[26].Value = V(m.CodTipoAntena); p[27].Value = V(m.CodEquipamentoAntena);
        p[28].Value = V(m.CodEquipamentoTransmissor); p[29].Value = V(m.PotenciaDbm); p[30].Value = V(m.AzimuteValido);
        p[31].Value = V(m.ElevacaoValida);
        _insEmissao.ExecuteNonQuery();
    }

    public void Commit()
    {
        _tx.Commit();
        _tx.Dispose();
        _tx = null!;
    }

    public void Dispose()
    {
        _insEstacao.Dispose();
        _insEmissao.Dispose();
        _tx?.Dispose();
        _conn.Dispose();
    }
}
