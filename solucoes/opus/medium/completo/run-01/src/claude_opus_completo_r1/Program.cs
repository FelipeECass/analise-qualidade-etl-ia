using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AnatelEtl;

public static class Program
{
    public const string ProjectName = "claude_opus_completo_r1";
    private const string DefaultRoot = @"D:\Projects\TCC";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run();
            Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERRO: {ex}");
            return 1;
        }
    }

    /// <summary>Localiza a raiz do espaço de trabalho (pasta com files/ e src/DDL.sql).</summary>
    private static string FindRoot()
    {
        static bool IsRoot(string d) =>
            Directory.Exists(Path.Combine(d, "files")) && File.Exists(Path.Combine(d, "src", "DDL.sql"));

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
                if (IsRoot(d.FullName)) return d.FullName;
        }
        if (IsRoot(DefaultRoot)) return DefaultRoot;
        throw new DirectoryNotFoundException($"Não foi possível localizar a pasta do projeto ({DefaultRoot}) com files/ e src/DDL.sql.");
    }

    private static void Run()
    {
        var sw = Stopwatch.StartNew();
        var root = FindRoot();
        var filesDir = Path.Combine(root, "files");
        var ddlPath = Path.Combine(root, "src", "DDL.sql");
        var dbDir = Path.Combine(root, "db");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, ProjectName + ".db");

        var zips = Directory.GetFiles(filesDir, "*.zip")
            .Where(p => Path.GetFileNameWithoutExtension(p).Length == 2)
            .OrderBy(p => Path.GetFileName(p).ToUpperInvariant(), StringComparer.Ordinal)
            .ToList();
        if (zips.Count == 0) throw new FileNotFoundException($"Nenhum ZIP encontrado em {filesDir}");

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix);

        var ddl = File.ReadAllText(ddlPath, Encoding.UTF8);

        long read = 0, rejected = 0, duplicated = 0, emissoes = 0;
        var stations = new HashSet<(string, string, string)>();
        var emissionKeys = new HashSet<UInt128>();
        var keyBuilder = new StringBuilder(256);

        using (var loader = new SqliteLoader(dbPath, ddl))
        {
            var fields = new List<string>(64);
            foreach (var zipPath in zips)
            {
                long fileRows = 0;
                using var zip = ZipFile.OpenRead(zipPath);
                var entries = zip.Entries.Where(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToList();
                if (entries.Count == 0) entries = zip.Entries.Where(e => e.Length > 0).ToList();
                if (entries.Count != 1) throw new InvalidDataException($"{zipPath}: esperado um único CSV, encontrados {entries.Count}.");

                using var stream = entries[0].Open();
                using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
                var csv = new CsvReader(reader, '|');
                if (!csv.ReadRecord(fields)) continue;
                var transformer = new Transformer(fields.ToList());

                while (csv.ReadRecord(fields))
                {
                    if (fields.Count == 1 && fields[0].Length == 0) continue; // linha em branco
                    read++; fileRows++;
                    var key = transformer.Key(fields);
                    if (key is null) { rejected++; continue; }

                    var em = transformer.BuildEmissao(fields, key.Value);
                    if (!emissionKeys.Add(EmissionHash(em, keyBuilder))) { duplicated++; continue; }

                    if (stations.Add(key.Value))
                        loader.Insert(transformer.BuildEstacao(fields, key.Value));
                    loader.Insert(em);
                    emissoes++;
                }
                Console.WriteLine($"{Path.GetFileName(zipPath)}: {fileRows} linhas ({sw.Elapsed:hh\\:mm\\:ss})");
            }
            loader.Commit();
        }
        SqliteConnection.ClearAllPools();

        Console.WriteLine($"Linhas lidas: {read}; rejeitadas: {rejected}; emissões duplicadas: {duplicated}");
        Console.WriteLine($"Estações gravadas: {stations.Count}; emissões gravadas: {emissoes}");
        Console.WriteLine($"Banco: {dbPath} ({sw.Elapsed:hh\\:mm\\:ss})");
    }

    /// <summary>Hash 128 bits da chave de duplicidade da emissão (valores tratados; NULL = NULL).</summary>
    private static UInt128 EmissionHash(Emissao m, StringBuilder sb)
    {
        sb.Clear();
        void S(string? s) { if (s is null) sb.Append('\u0001'); else sb.Append('\u0002').Append(s); sb.Append('\u0000'); }
        void D(double? d)
        {
            if (d is null) sb.Append('\u0001');
            else sb.Append('\u0002').Append((d.Value == 0 ? 0.0 : d.Value).ToString("R", CultureInfo.InvariantCulture));
            sb.Append('\u0000');
        }
        S(m.Uf); S(m.Fistel); S(m.NumEstacao); S(m.NumAto); D(m.Azimute);
        D(m.FreqTxMhz); D(m.FreqRxMhz); S(m.DesignacaoEmissao); S(m.Tecnologia);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return XxHash128.HashToUInt128(bytes);
    }
}
