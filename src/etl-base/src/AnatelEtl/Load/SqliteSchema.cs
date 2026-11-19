using System.Data;
using System.Reflection;
using Dapper;

namespace AnatelEtl.Load;

/// <summary>Cria as tabelas a partir do arquivo <c>Sql/schema.sql</c>, embutido no assembly.</summary>
public static class SqliteSchema
{
    private const string ResourceName = "AnatelEtl.Sql.schema.sql";

    private static readonly Lazy<string> Script = new(ReadScript);

    public static void EnsureCreated(IDbConnection connection, IDbTransaction? transaction = null) =>
        connection.Execute(Script.Value, transaction: transaction);

    private static string ReadScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Recurso embutido '{ResourceName}' não encontrado.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
