using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace ChatgptCompleto;

internal static class Program
{
    private static readonly DateOnly ReferenceDate = new(2026, 10, 15);

    private static int Main()
    {
        try
        {
            var root = FindRoot();
            var projectName = "chatgpt_gpt6_completo_r1";
            var databaseDirectory = Path.Combine(root, "db");
            Directory.CreateDirectory(databaseDirectory);
            var databasePath = Path.Combine(databaseDirectory, projectName + ".db");
            if (File.Exists(databasePath)) File.Delete(databasePath);

            using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True");
            connection.Open();
            using (var ddl = connection.CreateCommand())
            {
                ddl.CommandText = File.ReadAllText(Path.Combine(root, "src", "DDL.sql"), Encoding.UTF8);
                ddl.ExecuteNonQuery();
            }

            using var transaction = connection.BeginTransaction();
            var loader = new DatabaseLoader(connection, transaction);
            foreach (var zipPath in Directory.GetFiles(Path.Combine(root, "files"), "*.zip").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name) && e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"Nenhum CSV encontrado em {Path.GetFileName(zipPath)}.");
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
                using var csv = new CsvReader(reader);
                var headers = csv.ReadRecord() ?? throw new InvalidDataException($"CSV vazio: {entry.FullName}");
                var map = headers.Select((h, i) => (Name: h.Trim(), Index: i)).ToDictionary(x => x.Name, x => x.Index, StringComparer.Ordinal);
                foreach (var required in RequiredHeaders)
                    if (!map.ContainsKey(required)) throw new InvalidDataException($"Coluna obrigatória ausente em {entry.FullName}: {required}");

                string[]? row;
                while ((row = csv.ReadRecord()) is not null)
                {
                    var record = new RawRecord(map, row);
                    loader.Load(RowTransformer.Transform(record));
                }
            }
            transaction.Commit();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "files")) && File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql"))) return dir.FullName;
        throw new DirectoryNotFoundException("Não foi possível localizar a raiz que contém files/ e src/DDL.sql.");
    }

    private static readonly string[] RequiredHeaders =
    [
        "NumFistel", "NumEstacao", "SiglaUf", "NumAto", "CodMunicipio", "Municipio.NomeMunicipio",
        "NomeEntidade", "EnderecoEstacao", "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao",
        "FreqTxMHz", "FreqRxMHz", "Azimute", "PotenciaTransmissorWatts", "GanhoAntena", "AlturaAntena",
        "AnguloElevacao", "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao", "CodTipoClasseEstacao",
        "CodTipoAntena", "CodEquipamentoAntena", "CodEquipamentoTransmissor", "Status.state",
        "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade"
    ];
}

internal sealed class CsvReader(TextReader reader) : IDisposable
{
    public string[]? ReadRecord()
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var started = false;
        while (true)
        {
            var n = reader.Read();
            if (n < 0)
            {
                if (!started && fields.Count == 0 && field.Length == 0) return null;
                if (quoted) throw new InvalidDataException("Campo CSV entre aspas não foi terminado.");
                fields.Add(field.ToString());
                return fields.ToArray();
            }
            var c = (char)n;
            started = true;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else quoted = false;
                }
                else field.Append(c);
            }
            else if (c == '|' ) { fields.Add(field.ToString()); field.Clear(); }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == '\n') { fields.Add(field.ToString().TrimEnd('\r')); return fields.ToArray(); }
            else field.Append(c);
        }
    }
    public void Dispose() => reader.Dispose();
}

internal sealed class RawRecord(IReadOnlyDictionary<string, int> map, string[] values)
{
    public string? Get(string name) => map.TryGetValue(name, out var i) && i < values.Length ? Clean(values[i]) : null;
    private static string? Clean(string? value) { var s = value?.Trim(); return string.IsNullOrEmpty(s) ? null : s; }
}

internal sealed record Station(string Uf, string Fistel, string NumEstacao, string? CodMunicipio, string? Municipio,
    string? Endereco, double? Latitude, double? Longitude, int CoordenadaValida, string? Entidade, string Operadora);

internal sealed record Emission(string Uf, string Fistel, string NumEstacao, string? NumAto, string? Tecnologia,
    string Geracao, string? Designacao, double? Largura, double? Tx, double? Rx, double? Duplex, double? Azimute,
    double? Potencia, double? Ganho, double? Altura, string? Licenciamento, string? PrimeiroLicenciamento,
    string? Validade, int Vencida, double? Anos, string? Status, string? Classe, string? Polarizacao,
    double? Elevacao, double? MeiaPotencia, double? FrenteCosta, string? CodAntena, string? EquipAntena,
    string? EquipTransmissor, double? PotenciaDbm, int? AzimuteValido, int? ElevacaoValida);

internal sealed record TransformedRow(Station Station, Emission Emission, EmissionKey EmissionKey);

internal readonly record struct EmissionKey(string Uf, string Fistel, string NumEstacao, string? NumAto,
    double? Azimute, double? Tx, double? Rx, string? Designacao, string? Tecnologia)
{
    public static EmissionKey From(Emission e) => new(e.Uf, e.Fistel, e.NumEstacao, e.NumAto, e.Azimute,
        e.Tx, e.Rx, e.Designacao, e.Tecnologia);
}

internal static class RowTransformer
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly Regex NonAlphaNumeric = new("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex Band = new("^(\\d+)([HKMG])(\\d*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static TransformedRow? Transform(RawRecord r)
    {
        var fistel = r.Get("NumFistel"); var stationNumber = r.Get("NumEstacao"); var uf = r.Get("SiglaUf")?.ToUpperInvariant();
        if (fistel is null || stationNumber is null || uf is null) return null;
        var latitude = Number(r.Get("Latitude")); var longitude = Number(r.Get("Longitude"));
        var entity = Normalize(r.Get("NomeEntidade")); var address = Normalize(r.Get("EnderecoEstacao"));
        var station = new Station(uf, fistel, stationNumber, r.Get("CodMunicipio"), r.Get("Municipio.NomeMunicipio"),
            address, latitude, longitude, ValidCoordinate(latitude, longitude), entity, Operator(entity));

        var tx = Number(r.Get("FreqTxMHz")); var rx = Number(r.Get("FreqRxMHz")); var azimuth = Number(r.Get("Azimute"));
        var power = Number(r.Get("PotenciaTransmissorWatts")); var elevation = Number(r.Get("AnguloElevacao"));
        var firstDate = Date(r.Get("DataPrimeiroLicenciamento")); var expiry = Date(r.Get("DataValidade"));
        var technology = r.Get("Tecnologia")?.ToUpperInvariant(); var designation = r.Get("DesignacaoEmissao")?.ToUpperInvariant();
        var emission = new Emission(uf, fistel, stationNumber, r.Get("NumAto"), technology, Generation(technology), designation,
            Bandwidth(designation), tx, rx, Duplex(r.Get("FreqTxMHz"), r.Get("FreqRxMHz")),
            azimuth, power, Number(r.Get("GanhoAntena")), Number(r.Get("AlturaAntena")), Date(r.Get("DataLicenciamento")),
            firstDate, expiry, expiry is not null && DateOnly.ParseExact(expiry, "yyyy-MM-dd") < Today ? 1 : 0,
            firstDate is null ? null : Round((Today.DayNumber - DateOnly.ParseExact(firstDate, "yyyy-MM-dd").DayNumber) / 365.25, 1),
            r.Get("Status.state"), r.Get("CodTipoClasseEstacao"), r.Get("Polarizacao")?.ToUpperInvariant(), elevation,
            Number(r.Get("AnguloMeiaPotenciaAntena")), Number(r.Get("FrenteCostaAntena")), r.Get("CodTipoAntena"),
            r.Get("CodEquipamentoAntena"), r.Get("CodEquipamentoTransmissor"),
            power is > 0 ? Round(10 * Math.Log10(power.Value * 1000), 1) : null,
            azimuth is null ? null : azimuth is >= 0 and <= 360 ? 1 : 0,
            elevation is null ? null : elevation is >= -90 and <= 90 ? 1 : 0);
        return new TransformedRow(station, emission, EmissionKey.From(emission));
    }

    private static double Round(double x, int digits) => Math.Round(x, digits, MidpointRounding.AwayFromZero);
    private static double? Number(string? s)
    {
        if (s is null || s.Contains('.') && s.Contains(',')) return null;
        var normalized = s.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    }
    private static double? Duplex(string? tx, string? rx)
    {
        var txValue = DecimalNumber(tx); var rxValue = DecimalNumber(rx);
        if (!txValue.HasValue || !rxValue.HasValue) return null;
        // Os casos de aceitação informados usam empate para o par (0.1345 -> 0.134; -5.0625 -> -5.062).
        return (double)Math.Round(txValue.Value - rxValue.Value, 3, MidpointRounding.ToEven);
    }
    private static decimal? DecimalNumber(string? s)
    {
        if (s is null || s.Contains('.') && s.Contains(',')) return null;
        return decimal.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
    private static string? Date(string? s) => s is not null && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == s ? s : null;
    private static string? Normalize(string? s)
    {
        if (s is null) return null;
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var rune in decomposed.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)) b.Append(rune.ToString());
        }
        var result = Spaces.Replace(NonAlphaNumeric.Replace(b.ToString().ToUpperInvariant(), " "), " ").Trim();
        return result.Length == 0 ? null : result;
    }
    private static string Operator(string? e)
    {
        if (e is null) return "OUTRAS";
        bool Word(string w) => Regex.IsMatch(e, $@"\b{Regex.Escape(w)}\b", RegexOptions.CultureInvariant);
        if (Word("TELEFONICA") || Word("VIVO")) return "VIVO";
        if (Word("CLARO") || Word("EMBRATEL")) return "CLARO";
        if (Regex.IsMatch(e, "^TIM\\b")) return "TIM";
        if (Regex.IsMatch(e, "^OI\\b") || Word("TELEMAR") || Regex.IsMatch(e, @"\bBRASIL TELECOM\b")) return "OI";
        if (Word("ALGAR")) return "ALGAR";
        if (Word("SERCOMTEL")) return "SERCOMTEL";
        if (Word("BRISANET")) return "BRISANET";
        return "OUTRAS";
    }
    private static string Generation(string? t) => t switch { "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", null => "N/D", _ => "OUTRA" };
    private static double? Bandwidth(string? s)
    {
        if (s is null || s.Length < 4) return null;
        var m = Band.Match(s[..4]);
        if (!m.Success) return null;
        var unit = m.Groups[2].Value.ToUpperInvariant()[0];
        var scale = unit switch { 'H' => 0.001, 'K' => 1d, 'M' => 1000d, 'G' => 1_000_000d, _ => 0d };
        var fraction = m.Groups[3].Value;
        var value = double.Parse(m.Groups[1].Value + "." + (fraction.Length == 0 ? "0" : fraction), CultureInfo.InvariantCulture) * scale;
        return double.IsFinite(value) ? value : null;
    }
    private static int ValidCoordinate(double? lat, double? lon) => lat.HasValue && lon.HasValue && !(lat == 0 && lon == 0) && lat is >= -33.75 and <= 5.28 && lon is >= -73.99 and <= -28.83 ? 1 : 0;
}

internal sealed class DatabaseLoader
{
    private readonly SqliteConnection connection; private readonly SqliteTransaction transaction;
    private readonly HashSet<string> stations = new(StringComparer.Ordinal); private readonly HashSet<EmissionKey> emissions = [];
    private readonly SqliteCommand stationCommand; private readonly SqliteCommand emissionCommand;
    public DatabaseLoader(SqliteConnection connection, SqliteTransaction transaction)
    {
        this.connection = connection; this.transaction = transaction;
        stationCommand = connection.CreateCommand(); stationCommand.Transaction = transaction;
        stationCommand.CommandText = "INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES ($f,$n,$u,$cm,$m,$e,$lat,$lon,$cv,$ent,$op)";
        emissionCommand = connection.CreateCommand(); emissionCommand.Transaction = transaction;
        emissionCommand.CommandText = "INSERT INTO emissao (uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida) VALUES ($u,$f,$n,$ato,$tec,$g,$d,$lb,$tx,$rx,$dup,$az,$p,$ga,$ha,$dl,$dp,$dv,$v,$anos,$s,$cl,$pol,$el,$amp,$fc,$ct,$ea,$et,$pdb,$av,$ev)";
    }
    public void Load(TransformedRow? row)
    {
        if (row is null) return;
        var s = row.Station; var stationKey = string.Join("\u001f", s.Uf, s.Fistel, s.NumEstacao);
        if (stations.Add(stationKey))
        {
            Bind(stationCommand, ("$f",s.Fistel),("$n",s.NumEstacao),("$u",s.Uf),("$cm",s.CodMunicipio),("$m",s.Municipio),("$e",s.Endereco),("$lat",s.Latitude),("$lon",s.Longitude),("$cv",s.CoordenadaValida),("$ent",s.Entidade),("$op",s.Operadora)); stationCommand.ExecuteNonQuery();
        }
        if (!emissions.Add(row.EmissionKey)) return;
        var e = row.Emission;
        Bind(emissionCommand,("$u",e.Uf),("$f",e.Fistel),("$n",e.NumEstacao),("$ato",e.NumAto),("$tec",e.Tecnologia),("$g",e.Geracao),("$d",e.Designacao),("$lb",e.Largura),("$tx",e.Tx),("$rx",e.Rx),("$dup",e.Duplex),("$az",e.Azimute),("$p",e.Potencia),("$ga",e.Ganho),("$ha",e.Altura),("$dl",e.Licenciamento),("$dp",e.PrimeiroLicenciamento),("$dv",e.Validade),("$v",e.Vencida),("$anos",e.Anos),("$s",e.Status),("$cl",e.Classe),("$pol",e.Polarizacao),("$el",e.Elevacao),("$amp",e.MeiaPotencia),("$fc",e.FrenteCosta),("$ct",e.CodAntena),("$ea",e.EquipAntena),("$et",e.EquipTransmissor),("$pdb",e.PotenciaDbm),("$av",e.AzimuteValido),("$ev",e.ElevacaoValida)); emissionCommand.ExecuteNonQuery();
    }
    private static void Bind(SqliteCommand command, params (string Name, object? Value)[] values)
    {
        command.Parameters.Clear();
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
