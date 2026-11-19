using Microsoft.Data.Sqlite;

namespace AnatelEtl;

/// <summary>Cria o banco a partir do DDL.sql (sem alterações) e grava estações e emissões.</summary>
public sealed class SqliteLoader : IDisposable
{
    private readonly SqliteConnection _conn;
    private SqliteTransaction? _tx;
    private readonly SqliteCommand _insEstacao;
    private readonly SqliteCommand _insEmissao;
    private readonly SqliteParameter[] _pe;
    private readonly SqliteParameter[] _pm;

    private static readonly string[] EstacaoCols =
    {
        "fistel","num_estacao","uf","cod_municipio","municipio","endereco","latitude","longitude",
        "coordenada_valida","entidade","operadora",
    };

    private static readonly string[] EmissaoCols =
    {
        "uf","fistel","num_estacao","num_ato","tecnologia","geracao","designacao_emissao","largura_banda_khz",
        "freq_tx_mhz","freq_rx_mhz","duplex_mhz","azimute","potencia_w","ganho_antena","altura_antena_m",
        "data_licenciamento","data_primeiro_licenciamento","data_validade","vencida","anos_licenciada","status",
        "classe_estacao","polarizacao","angulo_elevacao_graus","angulo_meia_potencia_graus","frente_costa_db",
        "cod_tipo_antena","cod_equipamento_antena","cod_equipamento_transmissor","potencia_dbm","azimute_valido",
        "elevacao_valida",
    };

    public SqliteLoader(string dbPath, string ddlPath)
    {
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _conn.Open();

        // banco novo e recriável: prioriza velocidade de carga
        Exec("PRAGMA journal_mode = OFF;");
        Exec("PRAGMA synchronous = OFF;");
        Exec("PRAGMA temp_store = MEMORY;");
        Exec("PRAGMA cache_size = -200000;");
        Exec("PRAGMA foreign_keys = ON;");

        Exec(File.ReadAllText(ddlPath)); // esquema exatamente como no arquivo

        _insEstacao = Prepare("estacao", EstacaoCols, out _pe);
        _insEmissao = Prepare("emissao", EmissaoCols, out _pm);
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
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select((_, i) => "$p" + i))})";
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

    public void Begin()
    {
        _tx = _conn.BeginTransaction();
        _insEstacao.Transaction = _tx;
        _insEmissao.Transaction = _tx;
    }

    public void Commit()
    {
        _tx?.Commit();
        _tx?.Dispose();
        _tx = null;
        _insEstacao.Transaction = null;
        _insEmissao.Transaction = null;
    }

    private static object V(string? s) => s is null ? DBNull.Value : s;
    private static object V(double? d) => d.HasValue ? d.Value : DBNull.Value;
    private static object V(bool b) => b ? 1L : 0L;
    private static object V(bool? b) => b.HasValue ? (b.Value ? 1L : 0L) : DBNull.Value;
    private static object V(DateOnly? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : DBNull.Value;

    public void Insert(Estacao e)
    {
        _pe[0].Value = e.Fistel;
        _pe[1].Value = e.NumEstacao;
        _pe[2].Value = e.Uf;
        _pe[3].Value = V(e.CodMunicipio);
        _pe[4].Value = V(e.Municipio);
        _pe[5].Value = V(e.Endereco);
        _pe[6].Value = V(e.Latitude);
        _pe[7].Value = V(e.Longitude);
        _pe[8].Value = V(e.CoordenadaValida);
        _pe[9].Value = V(e.Entidade);
        _pe[10].Value = e.Operadora;
        _insEstacao.ExecuteNonQuery();
    }

    public void Insert(Emissao m)
    {
        int i = 0;
        _pm[i++].Value = m.Uf;
        _pm[i++].Value = m.Fistel;
        _pm[i++].Value = m.NumEstacao;
        _pm[i++].Value = V(m.NumAto);
        _pm[i++].Value = V(m.Tecnologia);
        _pm[i++].Value = m.Geracao;
        _pm[i++].Value = V(m.DesignacaoEmissao);
        _pm[i++].Value = V(m.LarguraBandaKhz);
        _pm[i++].Value = V(m.FreqTxMhz);
        _pm[i++].Value = V(m.FreqRxMhz);
        _pm[i++].Value = V(m.DuplexMhz);
        _pm[i++].Value = V(m.Azimute);
        _pm[i++].Value = V(m.PotenciaW);
        _pm[i++].Value = V(m.GanhoAntena);
        _pm[i++].Value = V(m.AlturaAntenaM);
        _pm[i++].Value = V(m.DataLicenciamento);
        _pm[i++].Value = V(m.DataPrimeiroLicenciamento);
        _pm[i++].Value = V(m.DataValidade);
        _pm[i++].Value = V(m.Vencida);
        _pm[i++].Value = V(m.AnosLicenciada);
        _pm[i++].Value = V(m.Status);
        _pm[i++].Value = V(m.ClasseEstacao);
        _pm[i++].Value = V(m.Polarizacao);
        _pm[i++].Value = V(m.AnguloElevacaoGraus);
        _pm[i++].Value = V(m.AnguloMeiaPotenciaGraus);
        _pm[i++].Value = V(m.FrenteCostaDb);
        _pm[i++].Value = V(m.CodTipoAntena);
        _pm[i++].Value = V(m.CodEquipamentoAntena);
        _pm[i++].Value = V(m.CodEquipamentoTransmissor);
        _pm[i++].Value = V(m.PotenciaDbm);
        _pm[i++].Value = V(m.AzimuteValido);
        _pm[i++].Value = V(m.ElevacaoValida);
        _insEmissao.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _tx?.Dispose();
        _insEstacao.Dispose();
        _insEmissao.Dispose();
        _conn.Dispose();
    }
}
