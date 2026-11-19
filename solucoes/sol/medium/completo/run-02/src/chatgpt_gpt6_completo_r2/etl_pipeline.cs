using System.Diagnostics;
using System.IO.Compression;

namespace AnatelEtl;

internal sealed class EtlPipeline(string root)
{
    internal static readonly string[] States =
    [
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA",
        "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO"
    ];
    private readonly HashSet<StationKey> stations = [];
    private long rows, rejected, duplicates, emissions;

    public void Run()
    {
        string input = Path.Combine(root, "files");
        string output = Path.Combine(root, "db", Program.OutputName + ".db");
        foreach (string uf in States)
            if (!File.Exists(Path.Combine(input, uf + ".zip")))
                throw new FileNotFoundException($"Arquivo de entrada ausente: {uf}.zip");
        string ddl = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"));
        string dbDirectory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(dbDirectory);
        string loading = Path.Combine(dbDirectory, Program.OutputName + "_loading_" + Guid.NewGuid().ToString("N") + ".db");
        var elapsed = Stopwatch.StartNew();
        var heartbeat = Stopwatch.StartNew();
        try
        {
            using (var writer = new SqliteWriter(loading, ddl, dbDirectory))
            {
                foreach (string uf in States)
                {
                    long initialRows = rows, initialEmissions = emissions;
                    try
                    {
                        using var zip = ZipFile.OpenRead(Path.Combine(input, uf + ".zip"));
                        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
                        if (entries.Length != 1 || !entries[0].Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("O ZIP deve conter exatamente um CSV.");
                        using var csv = new PipeCsvReader(entries[0].Open());
                        var header = csv.ReadRecord() ?? throw new InvalidDataException("CSV sem cabeçalho.");
                        var schema = new SourceSchema(header);
                        while (csv.ReadRecord() is { } row)
                        {
                            rows++;
                            if (row.Length > schema.Count)
                                throw new FormatException($"Registro {csv.RecordNumber} tem campos excedentes.");
                            Process(row, schema, writer);
                            if (rows % 25000 == 0) writer.NextBatch();
                            if (rows % 10000 == 0 && heartbeat.Elapsed.TotalSeconds >= 15)
                            {
                                Console.WriteLine($"Processando {uf}: {rows:N0} linhas, {emissions:N0} emissões, {elapsed.Elapsed}.");
                                heartbeat.Restart();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException($"{uf}.zip: {ex.Message}", ex);
                    }
                    writer.NextBatch();
                    Console.WriteLine($"{uf}: {rows - initialRows:N0} linhas, {emissions - initialEmissions:N0} emissões carregadas.");
                    heartbeat.Restart();
                }
                Console.WriteLine("Validando integridade e referências do banco...");
                writer.Finish(stations.Count, emissions);
            }
            File.Move(loading, output, overwrite: true);
            Console.WriteLine($"Concluído: {rows:N0} linhas; {stations.Count:N0} estações; {emissions:N0} emissões; " +
                $"{rejected:N0} rejeitadas; {duplicates:N0} duplicadas. Tempo: {elapsed.Elapsed}.");
            Console.WriteLine(output);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-journal", "-wal", "-shm" })
            {
                try { File.Delete(loading + suffix); }
                catch (IOException) { /* Preserve the original ETL error if cleanup is unavailable. */ }
            }
        }
    }

    internal void Process(string[] row, SourceSchema schema, SqliteWriter writer)
    {
        string? Get(Field field) => schema.Get(row, field);
        string? Text(Field field) => Transform.Clean(Get(field));
        double? Number(Field field) => Transform.Number(Get(field));
        string? fistel = Text(Field.Fistel), number = Text(Field.Station), uf = Transform.Upper(Get(Field.Uf));
        if (fistel is null || number is null || uf is null) { rejected++; return; }
        var stationKey = new StationKey(uf, fistel, number);
        if (stations.Add(stationKey))
        {
            double? lat = Number(Field.Latitude), lon = Number(Field.Longitude);
            string? entity = Transform.Normalize(Get(Field.Entity));
            writer.Station([
                fistel, number, uf, Text(Field.MunicipalityCode), Text(Field.Municipality),
                Transform.Normalize(Get(Field.Address)), lat, lon, Transform.Coordinates(lat, lon),
                entity, Transform.Operator(entity)
            ]);
        }
        var key = new EmissionKey(stationKey, Text(Field.Act), Number(Field.Azimuth), Number(Field.Tx),
            Number(Field.Rx), Transform.Upper(Get(Field.Designation)), Transform.Upper(Get(Field.Technology)));
        if (!writer.FirstEmission(key)) { duplicates++; return; }
        double? watts = Number(Field.Power), elevation = Number(Field.Elevation);
        DateOnly? licensed = Transform.Date(Get(Field.Licensed)), first = Transform.Date(Get(Field.FirstLicensed)),
            valid = Transform.Date(Get(Field.ValidUntil));
        writer.Emission([
            uf, fistel, number, key.Act, key.Technology, Transform.Generation(key.Technology), key.Designation,
            Transform.Bandwidth(key.Designation), key.Tx, key.Rx, Transform.Duplex(key.Tx, key.Rx), key.Azimuth,
            watts, Number(Field.Gain), Number(Field.Height), Transform.DateText(licensed), Transform.DateText(first),
            Transform.DateText(valid), Transform.Expired(valid), Transform.Years(first), Text(Field.Status),
            Text(Field.StationClass), Transform.Upper(Get(Field.Polarization)), elevation, Number(Field.BeamAngle),
            Number(Field.FrontBack), Text(Field.AntennaType), Text(Field.AntennaEquipment), Text(Field.TransmitterEquipment),
            Transform.Dbm(watts), Transform.InRange(key.Azimuth, 0, 360), Transform.InRange(elevation, -90, 90)
        ]);
        emissions++;
    }
}
