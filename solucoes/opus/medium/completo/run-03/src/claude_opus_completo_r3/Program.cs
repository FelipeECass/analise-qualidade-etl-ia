using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Etl;

try
{
    if (args.Length > 0 && args[0] == "--selftest")
        return SelfTest.Run();

    var sw = Stopwatch.StartNew();
    var root = Paths.FindRoot();
    var filesDir = Path.Combine(root, "files");
    var ddlPath = Path.Combine(root, "src", "DDL.sql");
    var dbPath = Path.Combine(root, "db", Paths.ProjectName + ".db");

    var zips = Directory.GetFiles(filesDir, "*.zip")
        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (zips.Count == 0) throw new FileNotFoundException("Nenhum arquivo ZIP encontrado em " + filesDir);

    Console.WriteLine($"Raiz: {root}");
    Console.WriteLine($"Banco: {dbPath}");

    long lidas = 0, rejeitadas = 0, duplicadas = 0, estacoes = 0, emissoes = 0;
    var stationKeys = new HashSet<(string, string, string)>();
    var emissionKeys = new HashSet<UInt128>();

    using (var loader = new SqliteLoader(dbPath, ddlPath))
    {
        foreach (var zipPath in zips)
        {
            long antes = lidas;
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(e => e.Length > 0)
                        ?? throw new InvalidDataException("ZIP sem CSV: " + zipPath);
            using var stream = entry.Open();
            using var sr = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
            var csv = new CsvReader(sr);
            var header = csv.Read() ?? throw new InvalidDataException("Arquivo vazio: " + zipPath);
            var tr = new Transformer(header);

            string[]? row;
            while ((row = csv.Read()) != null)
            {
                lidas++;
                var key = tr.StationKey(row);
                if (key is not { } k) { rejeitadas++; continue; }

                if (stationKeys.Add(k))
                {
                    loader.Insert(tr.BuildEstacao(row, k));
                    estacoes++;
                }

                var emi = tr.BuildEmissao(row, k);
                if (!emissionKeys.Add(Transformer.EmissionKey(emi))) { duplicadas++; continue; }
                loader.Insert(emi);
                emissoes++;
            }
            Console.WriteLine($"{Path.GetFileName(zipPath)}: {lidas - antes} linhas ({sw.Elapsed:hh\\:mm\\:ss})");
        }
        loader.Complete();
    }

    Console.WriteLine($"Linhas lidas: {lidas}; rejeitadas: {rejeitadas}; emissões duplicadas: {duplicadas}");
    Console.WriteLine($"Estações: {estacoes}; emissões: {emissoes}; tempo: {sw.Elapsed}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("ERRO: " + ex);
    return 1;
}
