using System.IO.Compression;
using System.Text;
using Etl.Csv;
using Etl.Load;
using Etl.Transform;

namespace Etl.Pipeline;

/// <summary>
/// Orquestra o ETL: lê os 27 ZIPs (AC a TO) em ordem, transforma cada linha e grava
/// o banco SQLite a partir do DDL, do zero.
/// </summary>
public sealed class EtlRunner
{
    private static readonly string[] Ufs =
    {
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA",
        "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    };

    private readonly string _root;
    private long _lidas;
    private long _rejeitadas;

    public EtlRunner(string root) => _root = root;

    /// <summary>Raiz do espaço de trabalho: primeiro ancestral de BaseDirectory com files/ e src/.</summary>
    public static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "files"))
                && File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Não foi possível localizar a raiz do espaço de trabalho (pasta com files/ e src/DDL.sql).");
    }

    public string Run()
    {
        var filesDir = Path.Combine(_root, "files");
        var ddlPath = Path.Combine(_root, "src", "DDL.sql");
        var dbPath = Path.Combine(_root, "db", "claude_haiku_completo_r1.db");

        var zips = Ufs.Select(uf => Path.Combine(filesDir, uf + ".zip")).ToList();
        var ausentes = zips.Where(z => !File.Exists(z)).Select(Path.GetFileName).ToList();
        if (ausentes.Count > 0)
        {
            throw new FileNotFoundException("ZIPs ausentes: " + string.Join(", ", ausentes));
        }
        if (!File.Exists(ddlPath))
        {
            throw new FileNotFoundException("DDL não encontrado.", ddlPath);
        }

        var ddl = File.ReadAllText(ddlPath);

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix);
        }

        using var loader = new SqliteLoader(dbPath, ddl);
        foreach (var zip in zips)
        {
            ProcessZip(zip, loader);
        }
        loader.Commit();

        return $"Linhas lidas: {_lidas}; rejeitadas: {_rejeitadas}; estações: {loader.StationsInserted}; " +
               $"emissões: {loader.EmissionsInserted}; duplicadas descartadas: {loader.DuplicateEmissions}. " +
               $"Banco: {dbPath}";
    }

    private void ProcessZip(string zipPath, SqliteLoader loader)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var csvs = zip.Entries
            .Where(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (csvs.Count != 1)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(zipPath)}: esperado 1 arquivo CSV, encontrado {csvs.Count}.");
        }

        using var stream = csvs[0].Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 20);
        using var csv = new CsvRecordReader(reader);

        var header = csv.ReadRecord()
                     ?? throw new InvalidDataException($"{Path.GetFileName(zipPath)}: arquivo sem cabeçalho.");
        var cols = ColumnMap.FromHeader(header);

        while (csv.ReadRecord() is { } record)
        {
            if (record.Length == 1 && record[0].Length == 0) continue; // linha em branco

            _lidas++;
            var mapped = RowMapper.Map(cols, record);
            if (mapped is null)
            {
                _rejeitadas++;
                continue;
            }
            loader.Add(mapped);
        }

        Console.WriteLine($"{Path.GetFileName(zipPath)} processado.");
    }
}
