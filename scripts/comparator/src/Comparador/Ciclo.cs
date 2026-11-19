using System.Diagnostics;
using System.Text;

namespace Comparador;

/// <summary>Compila e executa a solução. Devolve os erros (null se deu certo) e em que etapa ocorreram.</summary>
public static class Ciclo
{
    // Evita que uma solução travada segure o experimento indefinidamente.
    static readonly TimeSpan TempoMaximo = TimeSpan.FromHours(2);

    public static (string? Erros, string? Detalhe) Executar(string projeto, string banco, string log)
    {
        // Um banco de uma iteração anterior nunca pode ser comparado no lugar do novo.
        foreach (var sufixo in new[] { "", "-journal", "-wal", "-shm" }) File.Delete(banco + sufixo);

        if (!Directory.Exists(projeto) || Directory.GetFiles(projeto, "*.csproj").Length == 0)
            return ($"Projeto .NET não encontrado em {projeto}.", "código ausente");

        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        using var saida = new StreamWriter(log, false, Encoding.UTF8);

        var build = Rodar(["build", projeto, "-c", "Release", "-nologo", "-v", "q", "-clp:ErrorsOnly;NoSummary", "--disable-build-servers"], projeto, saida);
        if (build.Codigo != 0)
        {
            var linhas = build.Saida.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Contains(": error ")).Distinct();
            return (Texto(string.Join('\n', linhas), Texto(build.Saida, $"dotnet build terminou com código {build.Codigo}.")), "compilação");
        }

        var run = Rodar(["run", "--project", projeto, "-c", "Release", "--no-build"], projeto, saida);
        if (run.Estourou)
            return ($"A execução passou de {TempoMaximo.TotalMinutes} minutos e foi interrompida.", "execução");
        if (run.Codigo != 0)
            return (Texto(run.Erro, Texto(Ultimas(run.Saida, 50), $"O programa terminou com código {run.Codigo}.")), "execução");
        return (null, null);
    }

    static (int Codigo, string Saida, string Erro, bool Estourou) Rodar(string[] argumentos, string pasta, StreamWriter log)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = pasta,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in argumentos) psi.ArgumentList.Add(a);
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var p = Process.Start(psi)!;
        var saida = p.StandardOutput.ReadToEndAsync();
        var erro = p.StandardError.ReadToEndAsync();
        bool estourou = !p.WaitForExit(TempoMaximo);
        if (estourou) p.Kill(entireProcessTree: true);
        p.WaitForExit();

        log.WriteLine($"> dotnet {string.Join(' ', argumentos)}  (código {(estourou ? "tempo esgotado" : p.ExitCode)})");
        log.WriteLine(saida.Result);
        log.WriteLine(erro.Result);
        log.Flush();
        return (estourou ? -1 : p.ExitCode, saida.Result, erro.Result, estourou);
    }

    static string Texto(string s, string seVazio) => string.IsNullOrWhiteSpace(s) ? seVazio : s.Trim();

    static string Ultimas(string s, int n) => string.Join('\n', s.TrimEnd().Split('\n').TakeLast(n));
}
