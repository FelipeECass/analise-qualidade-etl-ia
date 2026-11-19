using System.Globalization;
using Microsoft.Data.Sqlite;

namespace claude_haiku_completo_r3.Persistencia;

/// <summary>
/// Gravação no SQLite. Cria o banco a partir do DDL sem alterações e mantém comandos
/// preparados para inserção em massa dentro de transações.
/// </summary>
public sealed class BancoSqlite : IDisposable
{
    private static readonly string[] ColunasEstacao =
    [
        "fistel", "num_estacao", "uf", "cod_municipio", "municipio", "endereco",
        "latitude", "longitude", "coordenada_valida", "entidade", "operadora",
    ];

    private static readonly string[] ColunasEmissao =
    [
        "uf", "fistel", "num_estacao", "num_ato", "tecnologia", "geracao", "designacao_emissao",
        "largura_banda_khz", "freq_tx_mhz", "freq_rx_mhz", "duplex_mhz", "azimute", "potencia_w",
        "ganho_antena", "altura_antena_m", "data_licenciamento", "data_primeiro_licenciamento",
        "data_validade", "vencida", "anos_licenciada", "status", "classe_estacao", "polarizacao",
        "angulo_elevacao_graus", "angulo_meia_potencia_graus", "frente_costa_db", "cod_tipo_antena",
        "cod_equipamento_antena", "cod_equipamento_transmissor", "potencia_dbm", "azimute_valido",
        "elevacao_valida",
    ];

    private readonly SqliteConnection _conexao;
    private readonly SqliteCommand _insEstacao;
    private readonly SqliteCommand _insEmissao;
    private SqliteTransaction? _transacao;

    private BancoSqlite(SqliteConnection conexao)
    {
        _conexao = conexao;
        _insEstacao = CriarInsert("estacao", ColunasEstacao);
        _insEmissao = CriarInsert("emissao", ColunasEmissao);
    }

    /// <summary>Recria o arquivo do banco a partir do DDL e abre a conexão para carga.</summary>
    public static BancoSqlite Criar(string caminhoBanco, string caminhoDdl)
    {
        if (!File.Exists(caminhoDdl))
            throw new FileNotFoundException("DDL não encontrado.", caminhoDdl);

        var pasta = Path.GetDirectoryName(caminhoBanco);
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

        foreach (var sufixo in new[] { "", "-journal", "-wal", "-shm" })
        {
            var arq = caminhoBanco + sufixo;
            if (File.Exists(arq)) File.Delete(arq);
        }

        var conexao = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = caminhoBanco,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        conexao.Open();

        // Só afeta velocidade de carga; não altera schema nem dados.
        using (var pragma = conexao.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF;";
            pragma.ExecuteNonQuery();
        }

        using (var ddl = conexao.CreateCommand())
        {
            ddl.CommandText = File.ReadAllText(caminhoDdl);
            ddl.ExecuteNonQuery();
        }

        return new BancoSqlite(conexao);
    }

    public void IniciarTransacao()
    {
        _transacao = _conexao.BeginTransaction();
        _insEstacao.Transaction = _transacao;
        _insEmissao.Transaction = _transacao;
    }

    public void ConfirmarTransacao()
    {
        _transacao?.Commit();
        _transacao?.Dispose();
        _transacao = null;
        _insEstacao.Transaction = null;
        _insEmissao.Transaction = null;
    }

    public void InserirEstacao(IReadOnlyList<object?> valores) => Executar(_insEstacao, valores);

    public void InserirEmissao(IReadOnlyList<object?> valores) => Executar(_insEmissao, valores);

    private void Executar(SqliteCommand comando, IReadOnlyList<object?> valores)
    {
        for (var i = 0; i < valores.Count; i++)
            comando.Parameters[i].Value = valores[i] ?? DBNull.Value;
        comando.ExecuteNonQuery();
    }

    private SqliteCommand CriarInsert(string tabela, string[] colunas)
    {
        var nomes = string.Join(", ", colunas);
        var parametros = string.Join(", ", colunas.Select(c => "@" + c));
        var cmd = _conexao.CreateCommand();
        cmd.CommandText = $"INSERT INTO {tabela} ({nomes}) VALUES ({parametros})";
        // Sem tipo explícito: o SQLite recebe o tipo real de cada valor (REAL, INTEGER, TEXT ou NULL).
        foreach (var c in colunas) cmd.Parameters.Add(new SqliteParameter("@" + c, DBNull.Value));
        cmd.Prepare();
        return cmd;
    }

    /// <summary>Formata data como yyyy-MM-dd para gravação em texto.</summary>
    public static string? FormatarData(DateOnly? data) =>
        data?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _transacao?.Dispose();
        _insEstacao.Dispose();
        _insEmissao.Dispose();
        _conexao.Dispose();
    }
}
