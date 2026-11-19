using System.IO.Compression;
using System.Text;

namespace Etl;

public sealed class Counters
{
    public long Read, Rejected, Stations, Emissions, DuplicateEmissions;
}

/// <summary>Fluxo principal: ZIPs → limpeza → rejeição → deduplicação → gravação.</summary>
public static class EtlRunner
{
    public static Counters Run(Paths paths, SqliteWriter writer, Action<string> log)
    {
        var zips = Directory.GetFiles(paths.FilesDir, "*.zip");
        Array.Sort(zips, StringComparer.Ordinal);
        if (zips.Length == 0) throw new FileNotFoundException($"Nenhum ZIP encontrado em {paths.FilesDir}");

        var dedup = new Deduplicator();
        var total = new Counters();

        foreach (var zip in zips)
        {
            long before = total.Read;
            ProcessZip(zip, writer, dedup, total);
            log($"{Path.GetFileName(zip)}: {total.Read - before:N0} linhas | acumulado: estacao={total.Stations:N0} emissao={total.Emissions:N0}");
        }
        return total;
    }

    private static void ProcessZip(string zipPath, SqliteWriter writer, Deduplicator dedup, Counters c)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => e.Length > 0 &&
                        e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(e => e.Length > 0)
                    ?? throw new InvalidDataException($"ZIP sem arquivo de dados: {zipPath}");

        using var stream = entry.Open();
        using var text = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16);
        using var csv = new CsvReader(text);

        var fields = new List<string>(48);
        if (!csv.ReadRecord(fields)) return;   // arquivo vazio
        var mapper = new RowMapper(fields);

        while (csv.ReadRecord(fields))
        {
            c.Read++;
            if (!mapper.TryMap(fields, out var station, out var emission))
            {
                c.Rejected++;
                continue;
            }

            if (dedup.IsNewStation(station))
            {
                writer.Insert(station);
                c.Stations++;
            }

            if (dedup.IsNewEmission(emission))
            {
                writer.Insert(emission);
                c.Emissions++;
            }
            else
            {
                c.DuplicateEmissions++;
            }
        }
    }
}
