namespace ClaudeHaikuCompletoR2;

/// <summary>Caminhos e constantes do pipeline.</summary>
internal static class Configuracao
{
    /// <summary>Raiz do espaço de trabalho no Windows.</summary>
    public const string RaizPadrao = @"D:\Projects\TCC";

    /// <summary>Variável de ambiente que sobrescreve a raiz (usada apenas para testes fora do Windows).</summary>
    public const string VariavelRaiz = "TCC_RAIZ";

    public const string NomeBanco = "claude_haiku_completo_r2.db";

    /// <summary>"Hoje" fixo, conforme a especificação.</summary>
    public static readonly DateOnly DataReferencia = new(2026, 10, 15);

    /// <summary>Unidades da federação, na ordem em que os arquivos são processados.</summary>
    public static readonly string[] UnidadesFederacao =
    [
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA",
        "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    ];

    private static readonly Lazy<string> _raiz = new(ResolverRaiz);

    /// <summary>
    /// Raiz do espaço de trabalho. Ordem: variável TCC_RAIZ; D:\Projects\TCC se existir;
    /// senão, a primeira pasta ancestral (do diretório do executável ou do atual) que contenha
    /// "files/" e "src/DDL.sql". Assim o programa funciona tanto no Windows quanto em um ambiente
    /// Linux em que o espaço de trabalho está montado em outro caminho.
    /// </summary>
    public static string Raiz => _raiz.Value;

    private static string ResolverRaiz()
    {
        var sobrescrita = Environment.GetEnvironmentVariable(VariavelRaiz);
        if (!string.IsNullOrWhiteSpace(sobrescrita)) return sobrescrita;

        if (EhRaizValida(RaizPadrao)) return RaizPadrao;

        var tentados = new List<string> { RaizPadrao };
        foreach (var inicio in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(inicio)); dir is not null; dir = dir.Parent)
            {
                tentados.Add(dir.FullName);
                if (EhRaizValida(dir.FullName)) return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Raiz do espaço de trabalho não encontrada (esperado: pastas 'files' e 'src/DDL.sql'). " +
            "Defina a variável TCC_RAIZ. Locais verificados: " + string.Join("; ", tentados.Distinct()));
    }

    private static bool EhRaizValida(string raiz) =>
        Directory.Exists(Path.Combine(raiz, "files")) && File.Exists(Path.Combine(raiz, "src", "DDL.sql"));

    public static string PastaArquivos => Path.Combine(Raiz, "files");

    public static string CaminhoBanco => Path.Combine(Raiz, "db", NomeBanco);

    public static string CaminhoDdl => Path.Combine(Raiz, "src", "DDL.sql");

    public static string CaminhoZip(string uf) => Path.Combine(PastaArquivos, uf + ".zip");

    // Retângulo do Brasil usado em coordenada_valida.
    public const double LatitudeMin = -33.75;
    public const double LatitudeMax = 5.28;
    public const double LongitudeMin = -73.99;
    public const double LongitudeMax = -28.83;
}
