using System.Data;
using Dapper;

namespace AnatelEtl.Load;

public sealed record GroupCount(string Grupo, long Total);

/// <summary>Consultas de conferência sobre os dados já carregados.</summary>
public sealed class AnalyticsQueries(IDbConnection connection)
{
    public IReadOnlyList<GroupCount> EmissoesPorGeracao(string uf) =>
        Query(
            """
            SELECT e.geracao AS Grupo, COUNT(*) AS Total
            FROM emissao e
            JOIN estacao s ON s.uf = e.uf AND s.fistel = e.fistel AND s.num_estacao = e.num_estacao
            WHERE e.uf = @uf
            GROUP BY e.geracao
            ORDER BY Total DESC;
            """,
            uf);

    public IReadOnlyList<GroupCount> EmissoesPorOperadora(string uf) =>
        Query(
            """
            SELECT s.operadora AS Grupo, COUNT(*) AS Total
            FROM emissao e
            JOIN estacao s ON s.uf = e.uf AND s.fistel = e.fistel AND s.num_estacao = e.num_estacao
            WHERE e.uf = @uf
            GROUP BY s.operadora
            ORDER BY Total DESC;
            """,
            uf);

    public IReadOnlyList<GroupCount> EmissoesPorFaixa(string uf) =>
        Query(
            """
            SELECT e.faixa_frequencia AS Grupo, COUNT(*) AS Total
            FROM emissao e
            JOIN estacao s ON s.uf = e.uf AND s.fistel = e.fistel AND s.num_estacao = e.num_estacao
            WHERE e.uf = @uf
            GROUP BY e.faixa_frequencia
            ORDER BY Total DESC;
            """,
            uf);

    private List<GroupCount> Query(string sql, string uf) =>
        connection.Query<GroupCount>(sql, new { uf }).AsList();
}
