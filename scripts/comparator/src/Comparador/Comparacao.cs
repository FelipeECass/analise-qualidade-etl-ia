using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Comparador;

public sealed record Exemplo(string Chave, string Esperado, string Obtido);

public sealed record ColunaDivergente(string Coluna, long Divergencias, List<Exemplo> Exemplos);

public sealed record ResultadoTabela(
    string Tabela, long LinhasGabarito, long LinhasSolucao, long Acertos, long Divergentes,
    long Ausentes, long Excedentes, double Taxa,
    List<string> ExemplosAusentes, List<string> ExemplosExcedentes, List<ColunaDivergente> Colunas);

public sealed class Resultado
{
    public required string GabaritoSha256 { get; init; }
    public string? SolucaoSha256 { get; set; }
    public List<string> ErrosEsquema { get; } = [];
    public List<string> Avisos { get; } = [];
    public List<ResultadoTabela> Tabelas { get; } = [];
    public long LinhasGabarito => Tabelas.Sum(t => t.LinhasGabarito);
    public long Acertos => Tabelas.Sum(t => t.Acertos);
    public long Divergentes => Tabelas.Sum(t => t.Divergentes);
    public long Ausentes => Tabelas.Sum(t => t.Ausentes);
    public long Excedentes => Tabelas.Sum(t => t.Excedentes);
    public double Taxa { get; set; }
    public bool Aceita => Taxa >= Comparacao.Limite;
    public string? CategoriaFalha { get; set; }
    public string? DetalheFalha { get; set; }
    public string? Erros { get; set; }
}

/// <summary>
/// Compara o banco da solução com o gabarito. Pareia as linhas pela chave (NULL igual a NULL)
/// e compara coluna a coluna com o operador IS do SQLite, sobre todas as colunas exceto emissao.id.
/// </summary>
public static class Comparacao
{
    public const double Limite = 0.98;
    public const string FalhaCodigo = "erro de compilação ou ausência de código gerado";
    public const string FalhaInstrucoes = "descumprimento das instruções";
    public const string FalhaResultado = "resultado incorreto";

    public static readonly string[] Tabelas = ["estacao", "emissao"];

    static readonly Dictionary<string, string[]> Chaves = new()
    {
        ["estacao"] = ["uf", "fistel", "num_estacao"],
        ["emissao"] = ["uf", "fistel", "num_estacao", "num_ato", "azimute", "freq_tx_mhz", "freq_rx_mhz", "designacao_emissao", "tecnologia"],
    };

    sealed record Col(string Nome, string Tipo, bool NotNull, long Pk);

    sealed record Esquema(List<string> Tabelas, Dictionary<string, List<Col>> Colunas, Dictionary<string, SortedSet<string>> Extras);

    public static Resultado Comparar(string ddl, string gabarito, string? solucao, string trabalho,
        string? erros = null, string? detalheErro = null)
    {
        var res = new Resultado { GabaritoSha256 = Sha256(gabarito) };
        if (erros is not null)
            return Falha(res, FalhaCodigo, detalheErro ?? "compilação ou execução", erros);
        if (solucao is null || !File.Exists(solucao))
            return Falha(res, FalhaCodigo, "banco inexistente", $"O banco de saída não foi gerado: {solucao}");
        res.SolucaoSha256 = Sha256(solucao);

        Directory.CreateDirectory(trabalho);
        var arquivo = Path.Combine(trabalho, "_trabalho.db");
        ApagarBanco(arquivo);
        try
        {
            using var referencia = Abrir(":memory:");
            Executar(referencia, File.ReadAllText(ddl));
            var esperado = Descrever(referencia, "main");

            // Abrir por URI habilita URIs no ATTACH, necessário para anexar os dois bancos como somente leitura.
            using var con = Abrir(new Uri(Path.GetFullPath(arquivo)).AbsoluteUri);
            Executar(con, "PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF; PRAGMA cache_size = -262144;");
            Anexar(con, gabarito, "g");
            Anexar(con, solucao, "s");

            var errosGabarito = Diferencas(esperado, Descrever(con, "g"), []);
            if (errosGabarito.Count > 0)
                throw new InvalidOperationException("O gabarito não segue o DDL: " + string.Join(" ", errosGabarito));

            res.ErrosEsquema.AddRange(Diferencas(esperado, Descrever(con, "s"), res.Avisos));
            if (res.ErrosEsquema.Count > 0)
                return Falha(res, FalhaInstrucoes, "esquema divergente", null);

            ConferirChavesDoGabarito(con, res.GabaritoSha256, trabalho);
            foreach (var t in Tabelas)
            {
                var colunas = esperado.Colunas[t].Select(c => c.Nome).Where(c => !(t == "emissao" && c == "id")).ToArray();
                res.Tabelas.Add(CompararTabela(con, t, colunas));
            }

            long denominador = res.LinhasGabarito + res.Excedentes;
            res.Taxa = denominador == 0 ? 0 : (double)res.Acertos / denominador;
            if (!res.Aceita) res.CategoriaFalha = FalhaResultado;
            return res;
        }
        finally
        {
            ApagarBanco(arquivo);
        }
    }

    static ResultadoTabela CompararTabela(SqliteConnection con, string t, string[] colunas)
    {
        var chave = Chaves[t];
        var lista = string.Join(", ", chave);
        string Prefixo(string a) => string.Join(", ", chave.Select(k => $"{a}.{k}"));
        static string Iguais(string a, string b, IEnumerable<string> cs) => string.Join(" AND ", cs.Select(c => $"{a}.{c} IS {b}.{c}"));

        // sk: chaves da solução, com o número da ocorrência; par: cada linha do gabarito com seu par (ocorrência 1); dv: pares divergentes.
        Executar(con, $"""
            DROP TABLE IF EXISTS sk; DROP TABLE IF EXISTS par; DROP TABLE IF EXISTS dv;
            CREATE TABLE sk AS SELECT rowid AS rid, {lista}, ROW_NUMBER() OVER (PARTITION BY {lista} ORDER BY rowid) AS n FROM s.{t};
            CREATE INDEX ix_sk ON sk ({lista});
            CREATE TABLE par AS SELECT a.rowid AS grid, (SELECT k.rid FROM sk k WHERE {Iguais("k", "a", chave)} AND k.n = 1) AS srid FROM g.{t} a;
            CREATE INDEX ix_par ON par (srid);
            CREATE TABLE dv AS SELECT p.grid, p.srid FROM par p
                JOIN g.{t} a ON a.rowid = p.grid JOIN s.{t} b ON b.rowid = p.srid
                WHERE NOT ({Iguais("a", "b", colunas)});
            """);

        long linhasG = Escalar(con, "SELECT count(*) FROM par");
        long pareadas = Escalar(con, "SELECT count(srid) FROM par");
        long linhasS = Escalar(con, "SELECT count(*) FROM sk");
        long divergentes = Escalar(con, "SELECT count(*) FROM dv");
        long ausentes = linhasG - pareadas, excedentes = linhasS - pareadas, acertos = pareadas - divergentes;

        const string JuntaDv = "FROM dv d JOIN g.{0} a ON a.rowid = d.grid JOIN s.{0} b ON b.rowid = d.srid";
        var colunasDivergentes = new List<ColunaDivergente>();
        if (divergentes > 0)
        {
            var somas = Linhas(con, $"SELECT {string.Join(", ", colunas.Select(c => $"coalesce(sum(a.{c} IS NOT b.{c}), 0)"))} {string.Format(JuntaDv, t)}",
                r => colunas.Select((_, i) => r.GetInt64(i)).ToArray()).Single();
            for (int i = 0; i < colunas.Length; i++)
            {
                if (somas[i] == 0) continue;
                var c = colunas[i];
                var exemplos = Linhas(con,
                    $"SELECT {Prefixo("a")}, a.{c}, b.{c} {string.Format(JuntaDv, t)} WHERE a.{c} IS NOT b.{c} ORDER BY d.grid LIMIT 5",
                    r => new Exemplo(Chave(r, chave), Formatar(r.GetValue(chave.Length)), Formatar(r.GetValue(chave.Length + 1))));
                colunasDivergentes.Add(new ColunaDivergente(c, somas[i], exemplos));
            }
        }

        var exemplosAusentes = Linhas(con,
            $"SELECT {Prefixo("a")} FROM par p JOIN g.{t} a ON a.rowid = p.grid WHERE p.srid IS NULL ORDER BY p.grid LIMIT 5",
            r => Chave(r, chave));
        var exemplosExcedentes = Linhas(con,
            $"SELECT {Prefixo("k")}, k.n FROM sk k WHERE k.n > 1 OR NOT EXISTS (SELECT 1 FROM par p WHERE p.srid = k.rid) ORDER BY k.rid LIMIT 5",
            r => Chave(r, chave) + (r.GetInt64(chave.Length) > 1 ? " (repetição de uma linha já presente)" : ""));

        long denominador = linhasG + excedentes;
        return new ResultadoTabela(t, linhasG, linhasS, acertos, divergentes, ausentes, excedentes,
            denominador == 0 ? 0 : (double)acertos / denominador, exemplosAusentes, exemplosExcedentes, colunasDivergentes);
    }

    static List<string> Diferencas(Esquema esp, Esquema sol, List<string> avisos)
    {
        var erros = new List<string>();
        foreach (var t in Tabelas)
        {
            if (!sol.Colunas.TryGetValue(t, out var cs))
            {
                erros.Add($"Tabela {t} não existe.");
                continue;
            }
            var ce = esp.Colunas[t];
            bool Tem(List<Col> l, string nome) => l.Any(x => x.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
            bool mesmasColunas = ce.Count == cs.Count && ce.All(c => Tem(cs, c.Nome));
            for (int i = 0; i < ce.Count; i++)
            {
                var c = ce[i];
                var j = cs.FindIndex(x => x.Nome.Equals(c.Nome, StringComparison.OrdinalIgnoreCase));
                if (j < 0) { erros.Add($"Tabela {t}: coluna {c.Nome} não existe."); continue; }
                var x = cs[j];
                if (mesmasColunas && j != i) erros.Add($"Tabela {t}: coluna {c.Nome} está na posição {j + 1}; o esperado é {i + 1}.");
                if (!x.Tipo.Equals(c.Tipo, StringComparison.OrdinalIgnoreCase)) erros.Add($"Tabela {t}: coluna {c.Nome} tem tipo '{x.Tipo}'; o esperado é '{c.Tipo}'.");
                if (x.NotNull != c.NotNull) erros.Add($"Tabela {t}: coluna {c.Nome} {(c.NotNull ? "deveria" : "não deveria")} ser NOT NULL.");
                if (x.Pk != c.Pk) erros.Add($"Tabela {t}: coluna {c.Nome} tem posição {x.Pk} na chave primária; o esperado é {c.Pk} (0 = fora da chave).");
            }
            foreach (var x in cs.Where(x => !Tem(ce, x.Nome)))
                erros.Add($"Tabela {t}: coluna {x.Nome} a mais.");

            foreach (var item in esp.Extras[t].Except(sol.Extras[t])) avisos.Add($"Tabela {t}: falta {item}.");
            foreach (var item in sol.Extras[t].Except(esp.Extras[t])) avisos.Add($"Tabela {t}: {item} a mais.");
        }
        foreach (var t in sol.Tabelas.Except(esp.Tabelas, StringComparer.OrdinalIgnoreCase))
            avisos.Add($"Tabela {t} a mais no banco.");
        return erros;
    }

    static Esquema Descrever(SqliteConnection con, string schema)
    {
        var tabelas = Linhas(con, $"SELECT name FROM {schema}.sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name",
            r => r.GetString(0));
        var colunas = new Dictionary<string, List<Col>>(StringComparer.OrdinalIgnoreCase);
        var extras = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tabelas)
        {
            var p = new Dictionary<string, object> { ["$t"] = t, ["$s"] = schema };
            colunas[t] = Linhas(con, "SELECT name, type, \"notnull\", pk FROM pragma_table_info($t, $s) ORDER BY cid",
                r => new Col(r.GetString(0), r.GetString(1), r.GetInt64(2) != 0, r.GetInt64(3)), p);

            var e = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var (nome, unico) in Linhas(con, "SELECT name, \"unique\" FROM pragma_index_list($t, $s)", r => (r.GetString(0), r.GetInt64(1) != 0), p))
            {
                var cols = Linhas(con, "SELECT name FROM pragma_index_info($i, $s) ORDER BY seqno", r => r.GetString(0),
                    new() { ["$i"] = nome, ["$s"] = schema });
                e.Add($"índice {(unico ? "único " : "")}({string.Join(", ", cols)})");
            }
            foreach (var fk in Linhas(con, "SELECT id, \"table\", \"from\", coalesce(\"to\", '') FROM pragma_foreign_key_list($t, $s) ORDER BY id, seq",
                         r => (Id: r.GetInt64(0), Tabela: r.GetString(1), De: r.GetString(2), Para: r.GetString(3)), p).GroupBy(f => f.Id))
                e.Add($"chave estrangeira ({string.Join(", ", fk.Select(f => f.De))}) → {fk.First().Tabela} ({string.Join(", ", fk.Select(f => f.Para))})");
            var sql = Linhas(con, $"SELECT sql FROM {schema}.sqlite_master WHERE type = 'table' AND name = $t", r => r.GetString(0), p).Single();
            if (sql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase)) e.Add("AUTOINCREMENT");
            extras[t] = e;
        }
        return new Esquema(tabelas, colunas, extras);
    }

    /// <summary>As chaves de pareamento precisam ser únicas no gabarito. Roda uma vez por versão do base.db.</summary>
    static void ConferirChavesDoGabarito(SqliteConnection con, string sha, string trabalho)
    {
        var marca = Path.Combine(trabalho, "gabarito_chaves_unicas.txt");
        if (File.Exists(marca) && File.ReadAllText(marca) == sha) return;
        foreach (var t in Tabelas)
        {
            long repetidas = Escalar(con, $"SELECT count(*) FROM (SELECT 1 FROM g.{t} GROUP BY {string.Join(", ", Chaves[t])} HAVING count(*) > 1)");
            if (repetidas > 0)
                throw new InvalidOperationException($"O gabarito tem {repetidas} chaves repetidas na tabela {t}; o pareamento não é válido.");
        }
        File.WriteAllText(marca, sha);
    }

    public static string Formatar(object v) => v switch
    {
        DBNull => "NULL",
        string s => $"\"{s}\"",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        byte[] b => $"BLOB de {b.Length} bytes",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    static string Chave(SqliteDataReader r, string[] chave) =>
        string.Join(", ", chave.Select((k, i) => $"{k}={Formatar(r.GetValue(i))}"));

    static Resultado Falha(Resultado r, string categoria, string detalhe, string? erros)
    {
        r.Taxa = 0;
        r.CategoriaFalha = categoria;
        r.DetalheFalha = detalhe;
        r.Erros = erros;
        return r;
    }

    static string Sha256(string arquivo)
    {
        using var f = File.OpenRead(arquivo);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    static SqliteConnection Abrir(string arquivo)
    {
        var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = arquivo, Pooling = false }.ToString());
        con.Open();
        return con;
    }

    static void Anexar(SqliteConnection con, string arquivo, string alias)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"ATTACH DATABASE $f AS {alias}";
        cmd.Parameters.AddWithValue("$f", new Uri(Path.GetFullPath(arquivo)).AbsoluteUri + "?mode=ro");
        cmd.ExecuteNonQuery();
    }

    static void ApagarBanco(string arquivo)
    {
        foreach (var sufixo in new[] { "", "-journal", "-wal", "-shm" }) File.Delete(arquivo + sufixo);
    }

    static void Executar(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        cmd.ExecuteNonQuery();
    }

    static long Escalar(SqliteConnection con, string sql) => Linhas(con, sql, r => r.GetInt64(0)).Single();

    static List<T> Linhas<T>(SqliteConnection con, string sql, Func<SqliteDataReader, T> ler, Dictionary<string, object>? parametros = null)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        foreach (var (k, v) in parametros ?? new()) cmd.Parameters.AddWithValue(k, v);
        using var r = cmd.ExecuteReader();
        var lista = new List<T>();
        while (r.Read()) lista.Add(ler(r));
        return lista;
    }
}
