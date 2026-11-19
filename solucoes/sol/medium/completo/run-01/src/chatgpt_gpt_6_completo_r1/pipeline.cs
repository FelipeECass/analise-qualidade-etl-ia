using System.Diagnostics;
using System.IO.Compression;

namespace AnatelEtl;

internal static class Pipeline
{
    internal const string Name = "chatgpt_gpt_6_completo_r1";
    private static readonly string[] States =
        ["AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA", "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO"];
    public static string FindRoot()
    {
        if (OperatingSystem.IsWindows()) return @"D:\Projects\TCC";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "DDL.sql")) && Directory.Exists(Path.Combine(directory.FullName, "files")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Não foi localizada a raiz com files e src/DDL.sql.");
    }
    public static void Run(string root)
    {
        string ddl = Path.Combine(root, "src", "DDL.sql");
        if (!File.Exists(ddl)) throw new FileNotFoundException("DDL.sql não encontrado.", ddl);
        string[] archives = States.Select(uf => Path.Combine(root, "files", uf + ".zip")).ToArray();
        foreach (string archive in archives)
            if (!File.Exists(archive)) throw new FileNotFoundException("ZIP obrigatório não encontrado.", archive);
        string output = Path.Combine(root, "db", Name + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        foreach (string suffix in new[] { "", "-journal", "-wal", "-shm" }) File.Delete(output + suffix);
        using var database = new DatabaseWriter(output, ddl);
        long read = 0, rejected = 0, duplicates = 0;
        var timer = Stopwatch.StartNew();
        foreach (string archive in archives)
        {
            long previous = database.Emissions;
            using var zip = ZipFile.OpenRead(archive);
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (entries.Length != 1 || !entries[0].Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{archive}: esperado exatamente um CSV.");
            using var stream = entries[0].Open();
            using var csv = new CsvReader(stream);
            var row = new SourceRow(csv.ReadRecord() ?? throw new InvalidDataException($"{archive}: CSV vazio."));
            database.Begin();
            while (csv.ReadRecord() is { } fields)
            {
                read++;
                row.Set(fields);
                string? fistel = row["NumFistel"], number = row["NumEstacao"], uf = Transform.Upper(row["SiglaUf"]);
                if (fistel is null || number is null || uf is null) { rejected++; continue; }
                string? act = row["NumAto"], technology = Transform.Upper(row["Tecnologia"]), designation = Transform.Upper(row["DesignacaoEmissao"]);
                double? tx = Transform.Number(row["FreqTxMHz"]), rx = Transform.Number(row["FreqRxMHz"]), azimuth = Transform.Number(row["Azimute"]);
                if (!database.FirstEmission(uf, fistel, number, act, azimuth, tx, rx, designation, technology)) { duplicates++; continue; }
                Load(database, row, uf, fistel, number, act, technology, designation, tx, rx, azimuth);
            }
            database.Commit();
            Console.WriteLine($"{Path.GetFileNameWithoutExtension(archive)}: {database.Emissions - previous:N0} emissões novas; tempo {timer.Elapsed:hh\\:mm\\:ss}");
        }
        database.Check();
        if (read != rejected + duplicates + database.Emissions) throw new InvalidDataException("Contagens inconsistentes.");
        Console.WriteLine($"Concluído: lidas={read}; rejeitadas={rejected}; duplicadas={duplicates}; estações={database.Stations}; emissões={database.Emissions}; duração={timer.Elapsed}.");
        Console.WriteLine(output);
    }
    private static void Load(DatabaseWriter database, SourceRow row, string uf, string fistel, string number, string? act,
        string? technology, string? designation, double? tx, double? rx, double? azimuth)
    {
        double? latitude = Transform.Number(row["Latitude"]), longitude = Transform.Number(row["Longitude"]);
        string? entity = Transform.Normalize(row["NomeEntidade"]);
        database.Station([
            fistel, number, uf, row["CodMunicipio"], row["Municipio.NomeMunicipio"], Transform.Normalize(row["EnderecoEstacao"]),
            latitude, longitude, Transform.Coordinates(latitude, longitude), entity, Transform.Operator(entity)
        ]);
        double? power = Transform.Number(row["PotenciaTransmissorWatts"]), elevation = Transform.Number(row["AnguloElevacao"]);
        DateOnly? first = Transform.Date(row["DataPrimeiroLicenciamento"]), expiry = Transform.Date(row["DataValidade"]);
        database.Emission([
            uf, fistel, number, act, technology, Transform.Generation(technology), designation, Transform.Bandwidth(designation),
            tx, rx, Transform.Duplex(tx, rx), azimuth, power, Transform.Number(row["GanhoAntena"]), Transform.Number(row["AlturaAntena"]),
            Transform.DateText(Transform.Date(row["DataLicenciamento"])), Transform.DateText(first), Transform.DateText(expiry),
            Transform.Expired(expiry), Transform.Years(first), row["Status.state"], row["CodTipoClasseEstacao"], Transform.Upper(row["Polarizacao"]),
            elevation, Transform.Number(row["AnguloMeiaPotenciaAntena"]), Transform.Number(row["FrenteCostaAntena"]),
            row["CodTipoAntena"], row["CodEquipamentoAntena"], row["CodEquipamentoTransmissor"], Transform.PowerDbm(power),
            Transform.InRange(azimuth, 0, 360), Transform.InRange(elevation, -90, 90)
        ]);
    }
}
