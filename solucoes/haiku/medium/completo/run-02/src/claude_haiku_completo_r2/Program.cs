using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace ClaudeHaikuCompletoR2;

/// <summary>
/// Pipeline ETL de licenciamento de estações (Anatel): lê os 27 ZIPs (um por UF), trata cada linha
/// e grava o banco SQLite com as tabelas estacao e emissao. Executa sem argumentos.
/// Sucesso: código 0. Falha: código diferente de 0 e mensagem em stderr.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        try
        {
            Executar();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERRO: " + ex);
            return 1;
        }
    }

    private static void Executar()
    {
        var cronometro = Stopwatch.StartNew();
        var caminhoBanco = Configuracao.CaminhoBanco;

        // Verificação prévia: falhar cedo se algum arquivo de entrada estiver faltando.
        foreach (var uf in Configuracao.UnidadesFederacao)
        {
            var zip = Configuracao.CaminhoZip(uf);
            if (!File.Exists(zip)) throw new FileNotFoundException("Arquivo de entrada não encontrado.", zip);
        }

        var ddl = File.ReadAllText(Configuracao.CaminhoDdl, Encoding.UTF8);
        ApagarBanco(caminhoBanco);

        using var carga = new CargaSqlite(caminhoBanco);
        carga.CriarEsquema(ddl);

        long lidas = 0, rejeitadas = 0;
        foreach (var uf in Configuracao.UnidadesFederacao)
        {
            var (l, r) = ProcessarUf(uf, carga);
            lidas += l;
            rejeitadas += r;
        }

        Console.WriteLine();
        Console.WriteLine($"Concluído em {cronometro.Elapsed}.");
        Console.WriteLine($"Linhas lidas: {lidas:N0}; rejeitadas: {rejeitadas:N0}.");
        Console.WriteLine($"Estações gravadas: {carga.EstacoesInseridas:N0}.");
        Console.WriteLine($"Emissões gravadas: {carga.EmissoesInseridas:N0}; duplicadas descartadas: {carga.EmissoesDuplicadas:N0}.");
        Console.WriteLine($"Banco: {caminhoBanco}");
    }

    /// <summary>Processa o CSV de uma UF. Devolve (linhas lidas, linhas rejeitadas).</summary>
    private static (long Lidas, long Rejeitadas) ProcessarUf(string uf, CargaSqlite carga)
    {
        var cronometro = Stopwatch.StartNew();
        using var zip = ZipFile.OpenRead(Configuracao.CaminhoZip(uf));

        // O nome do CSV não é conhecido de antemão: usa-se a única entrada .csv do ZIP.
        var entrada = zip.Entries
            .Where(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidDataException($"{uf}.zip não contém nenhum arquivo .csv.");

        using var fluxo = entrada.Open();
        using var leitor = new CsvLeitor(new StreamReader(
            fluxo,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1 << 16));

        var cabecalho = leitor.ProximoRegistro()
            ?? throw new InvalidDataException($"{uf}.zip: o CSV está vazio (sem cabeçalho).");
        var transformacao = new Transformacao(cabecalho);

        carga.IniciarLote();
        long lidas = 0, rejeitadas = 0;
        string[]? campos;
        while ((campos = leitor.ProximoRegistro()) is not null)
        {
            lidas++;
            if (campos.Length != cabecalho.Length)
            {
                throw new FormatException(
                    $"{uf}: linha {leitor.NumeroLinha} tem {campos.Length} campos; o cabeçalho tem {cabecalho.Length}.");
            }

            var linha = transformacao.Transformar(campos);
            if (linha is null)
            {
                rejeitadas++;
                continue;
            }

            carga.Gravar(linha);
        }

        carga.ConfirmarLote();
        Console.WriteLine($"{uf}: {lidas:N0} linhas, {rejeitadas:N0} rejeitadas ({cronometro.Elapsed:mm\\:ss}).");
        return (lidas, rejeitadas);
    }

    private static void ApagarBanco(string caminho)
    {
        foreach (var arquivo in new[] { caminho, caminho + "-journal", caminho + "-wal", caminho + "-shm" })
        {
            if (File.Exists(arquivo)) File.Delete(arquivo);
        }
    }
}
