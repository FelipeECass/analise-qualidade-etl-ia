using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using AnatelEtl;

try
{
    if (args.Contains("--selftest")) return SelfTest.Run();

    var sw = Stopwatch.StartNew();
    var root = Paths.ResolveRoot();
    var filesDir = Path.Combine(root, "files");
    var ddl = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"));
    var dbPath = Path.Combine(root, "db", Paths.DbName);

    var zips = Directory.GetFiles(filesDir, "*.zip").OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
    if (zips.Count == 0) throw new FileNotFoundException($"Nenhum arquivo ZIP encontrado em {filesDir}");

    var estacoes = new HashSet<string>(StringComparer.Ordinal);
    var dedup = new Deduplicator();
    long lidas = 0, rejeitadas = 0, duplicadas = 0, emissoes = 0;

    using (var loader = new SqliteLoader(dbPath, ddl))
    {
        foreach (var zipPath in zips)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(e => e.Length > 0)
                        ?? throw new InvalidDataException($"ZIP sem CSV: {zipPath}");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
            var csv = new CsvReader(reader, '|');
            var header = csv.ReadRecord() ?? throw new InvalidDataException($"CSV vazio: {zipPath}");
            var t = new Transformer(header);
            long fileRows = 0;

            string[]? row;
            while ((row = csv.ReadRecord()) != null)
            {
                lidas++; fileRows++;
                if (!t.TryGetKey(row, out var uf, out var fistel, out var num)) { rejeitadas++; continue; }

                var emissao = t.BuildEmissao(row, uf, fistel, num);
                if (!dedup.Add(emissao)) { duplicadas++; continue; }

                if (estacoes.Add(uf + "\u0001" + fistel + "\u0001" + num))
                    loader.Insert(t.BuildEstacao(row, uf, fistel, num));
                loader.Insert(emissao);
                emissoes++;
            }
            Console.WriteLine($"{Path.GetFileName(zipPath)}: {fileRows:N0} linhas ({sw.Elapsed:hh\\:mm\\:ss})");
        }
        loader.Complete();
    }

    Console.WriteLine($"Lidas: {lidas:N0}; rejeitadas: {rejeitadas:N0}; duplicadas: {duplicadas:N0}; " +
                      $"emissões: {emissoes:N0}; estações: {estacoes.Count:N0}; tempo: {sw.Elapsed:hh\\:mm\\:ss}");
    Console.WriteLine($"Banco: {dbPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("ERRO: " + ex);
    return 1;
}
