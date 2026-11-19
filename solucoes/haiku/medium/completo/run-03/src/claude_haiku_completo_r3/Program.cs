using System.Text;
using claude_haiku_completo_r3.Pipeline;

namespace claude_haiku_completo_r3;

/// <summary>
/// Ponto de entrada. Executado sem argumentos: lê files/&lt;UF&gt;.zip, aplica o ETL
/// e grava db/claude_haiku_completo_r3.db a partir de src/DDL.sql.
/// Sucesso → código 0. Falha → mensagem em stderr e código 1.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        try
        {
            var rotas = Rotas.Resolver();
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine($"Raiz: {rotas.Raiz}");

            var sumario = EtlPipeline.Executar(rotas);

            Console.WriteLine(sumario);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERRO: {ex}");
            return 1;
        }
    }
}

/// <summary>Caminhos de entrada e saída, relativos à raiz do workspace.</summary>
public sealed class Rotas
{
    // Raiz no Windows (D:\Projects\TCC). Em outros SO (ex.: WSL, onde o workspace é /workspace), usa o fallback.
    private const string RaizWindows = @"D:\Projects\TCC";
    private const string RaizFallback = "/workspace";

    public string Raiz { get; }
    public string PastaZips => Path.Combine(Raiz, "files");
    public string Ddl => Path.Combine(Raiz, "src", "DDL.sql");
    public string Banco => Path.Combine(Raiz, "db", "claude_haiku_completo_r3.db");

    private Rotas(string raiz) => Raiz = raiz;

    public static Rotas Resolver()
    {
        var raiz = Directory.Exists(RaizWindows) ? RaizWindows : RaizFallback;
        if (!Directory.Exists(raiz))
            throw new DirectoryNotFoundException($"Raiz do workspace não encontrada: {raiz}");
        return new Rotas(raiz);
    }
}
