using System.IO.Compression;
using System.Text;

namespace AnatelEtl;

public sealed class EtlStats
{
    public long LinhasLidas, Rejeitadas, Duplicadas, Estacoes, Emissoes;
}

/// <summary>Lê os ZIPs (um por UF), limpa, rejeita, deduplica e entrega ao loader.</summary>
public sealed class EtlProcessor
{
    private readonly SqliteLoader _loader;
    public EtlStats Stats { get; } = new();

    // Estado de deduplicação por UF. Como cada arquivo contém só a sua UF, o estado é liberado ao fim do arquivo;
    // se uma UF já liberada reaparecer em outro arquivo, falha explicitamente (em vez de deduplicar errado).
    private sealed class UfState
    {
        public readonly HashSet<StationKey> Stations = new();
        public readonly HashSet<EmissionKey> Emissions = new();
    }

    public EtlProcessor(SqliteLoader loader) => _loader = loader;

    public void Run(IEnumerable<string> zipFiles)
    {
        var released = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zip in zipFiles)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var before = (Stats.LinhasLidas, Stats.Estacoes, Stats.Emissoes);
            var states = new Dictionary<string, UfState>(StringComparer.Ordinal);

            _loader.Begin();
            ProcessZip(zip, states, released);
            _loader.Commit();

            foreach (var uf in states.Keys) released.Add(uf);
            Console.WriteLine($"{Path.GetFileName(zip),-8} linhas={Stats.LinhasLidas - before.LinhasLidas,9:N0} " +
                              $"estacoes={Stats.Estacoes - before.Estacoes,8:N0} emissoes={Stats.Emissoes - before.Emissoes,9:N0} " +
                              $"({sw.Elapsed.TotalSeconds:F1}s)");
        }
    }

    private void ProcessZip(string zipPath, Dictionary<string, UfState> states, HashSet<string> released)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Where(e => e.Length > 0 && !e.FullName.EndsWith('/')).ToList();
        var entry = entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ?? entries.FirstOrDefault()
                    ?? throw new InvalidDataException($"ZIP sem arquivo de dados: {zipPath}");

        using var stream = entry.Open();
        using var text = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        var csv = new PipeCsvReader(text);
        var fields = new List<string>(48);

        if (!csv.ReadRecord(fields)) throw new InvalidDataException($"Arquivo vazio: {zipPath}");
        var mapper = new RowMapper(fields);

        while (csv.ReadRecord(fields))
        {
            // linha totalmente em branco (sem campos úteis)
            if (fields.Count == 1 && fields[0].Length == 0) continue;
            Stats.LinhasLidas++;

            if (!mapper.TryMap(fields, out var est, out var emi)) { Stats.Rejeitadas++; continue; }

            if (!states.TryGetValue(est!.Uf, out var st))
            {
                if (released.Contains(est.Uf))
                    throw new InvalidDataException($"UF {est.Uf} aparece em mais de um arquivo ({Path.GetFileName(zipPath)}).");
                states[est.Uf] = st = new UfState();
            }

            var skey = new StationKey(est.Uf, est.Fistel, est.NumEstacao);
            if (st.Stations.Add(skey))
            {
                _loader.Insert(est);
                Stats.Estacoes++;
            }

            var ekey = new EmissionKey(emi!.Uf, emi.Fistel, emi.NumEstacao, emi.NumAto, emi.Azimute,
                emi.FreqTxMhz, emi.FreqRxMhz, emi.DesignacaoEmissao, emi.Tecnologia);
            if (!st.Emissions.Add(ekey)) { Stats.Duplicadas++; continue; }

            _loader.Insert(emi);
            Stats.Emissoes++;
        }
    }
}
