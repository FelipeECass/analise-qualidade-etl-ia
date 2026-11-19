namespace Etl;

/// <summary>Localiza a raiz do espaço de trabalho (pasta que contém src/DDL.sql e files/).</summary>
public static class Paths
{
    public const string ProjectName = "claude_opus_completo_r3";
    private const string FallbackRoot = @"D:\Projects\TCC";

    public static string FindRoot()
    {
        var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "files")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        if (Directory.Exists(FallbackRoot)) return FallbackRoot;
        throw new DirectoryNotFoundException(
            "Não foi possível localizar a pasta raiz (com src/DDL.sql e files/).");
    }
}
