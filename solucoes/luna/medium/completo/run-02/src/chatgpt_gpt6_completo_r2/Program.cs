using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.Data.Sqlite;

internal static class Program
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void Run()
    {
        var root = FindRoot();
        var inputDir = Path.Combine(root, "files");
        var ddlPath = Path.Combine(root, "src", "DDL.sql");
        var outputDir = Path.Combine(root, "db");
        Directory.CreateDirectory(outputDir);
        var dbPath = Path.Combine(outputDir, "chatgpt_gpt6_completo_r2.db");
        if (File.Exists(dbPath)) File.Delete(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using (var ddl = connection.CreateCommand())
        {
            ddl.CommandText = File.ReadAllText(ddlPath, Encoding.UTF8);
            ddl.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        using var stationCommand = MakeStationCommand(connection, transaction);
        using var emissionCommand = MakeEmissionCommand(connection, transaction);
        var stations = new HashSet<StationKey>();
        var emissions = new HashSet<EmissionKey>();

        foreach (var zipPath in Directory.GetFiles(inputDir, "*.zip").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var csvEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (csvEntries.Length != 1) throw new InvalidDataException($"{Path.GetFileName(zipPath)} deve conter exatamente um CSV.");
            using var stream = csvEntries[0].Open();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            using var csv = new DelimitedReader(reader, '|');
            var header = csv.ReadRecord() ?? throw new InvalidDataException($"CSV vazio em {zipPath}.");
            var indexes = header.Select((name, index) => (name, index)).ToDictionary(x => x.name.TrimStart('\uFEFF'), x => x.index, StringComparer.Ordinal);
            foreach (var required in RequiredColumns)
                if (!indexes.ContainsKey(required)) throw new InvalidDataException($"Coluna obrigatória ausente: {required} em {zipPath}.");

            string? Get(string[] row, string name)
            {
                var i = indexes[name];
                return i < row.Length ? Clean(row[i]) : null;
            }

            while (csv.ReadRecord() is { } row)
            {
                var uf = Get(row, "SiglaUf")?.ToUpperInvariant();
                var fistel = Get(row, "NumFistel");
                var numEstacao = Get(row, "NumEstacao");
                if (uf is null || fistel is null || numEstacao is null) continue;
                var stationKey = new StationKey(uf, fistel, numEstacao);
                var entity = NormalizeText(Get(row, "NomeEntidade"));
                var lat = ParseNumber(Get(row, "Latitude"));
                var lon = ParseNumber(Get(row, "Longitude"));
                if (stations.Add(stationKey))
                {
                    var coordValid = lat.HasValue && lon.HasValue && !(lat.Value == 0 && lon.Value == 0)
                        && lat.Value >= -33.75 && lat.Value <= 5.28 && lon.Value >= -73.99 && lon.Value <= -28.83;
                    Bind(stationCommand,
                        ("$fistel", fistel), ("$num_estacao", numEstacao), ("$uf", uf),
                        ("$cod_municipio", Get(row, "CodMunicipio")), ("$municipio", Get(row, "Municipio.NomeMunicipio")),
                        ("$endereco", NormalizeText(Get(row, "EnderecoEstacao"))), ("$latitude", lat), ("$longitude", lon),
                        ("$coordenada_valida", coordValid ? 1 : 0), ("$entidade", entity), ("$operadora", Operator(entity)));
                    stationCommand.ExecuteNonQuery();
                }

                var numAto = Get(row, "NumAto");
                var tech = Get(row, "Tecnologia")?.ToUpperInvariant();
                var designation = Get(row, "DesignacaoEmissao")?.ToUpperInvariant();
                var tx = ParseNumber(Get(row, "FreqTxMHz"));
                var rx = ParseNumber(Get(row, "FreqRxMHz"));
                var az = ParseNumber(Get(row, "Azimute"));
                var watts = ParseNumber(Get(row, "PotenciaTransmissorWatts"));
                var gain = ParseNumber(Get(row, "GanhoAntena"));
                var height = ParseNumber(Get(row, "AlturaAntena"));
                var elevation = ParseNumber(Get(row, "AnguloElevacao"));
                var halfPower = ParseNumber(Get(row, "AnguloMeiaPotenciaAntena"));
                var frontBack = ParseNumber(Get(row, "FrenteCostaAntena"));
                var key = new EmissionKey(stationKey, numAto, az, tx, rx, designation, tech);
                if (!emissions.Add(key)) continue;

                var licensed = ParseDate(Get(row, "DataLicenciamento"));
                var firstLicensed = ParseDate(Get(row, "DataPrimeiroLicenciamento"));
                var validity = ParseDate(Get(row, "DataValidade"));
                double? years = firstLicensed is null ? null : Math.Round((Today.ToDateTime(TimeOnly.MinValue) - firstLicensed.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.25, 1, MidpointRounding.AwayFromZero);
                double? duplex = tx is null || rx is null ? null : Math.Round(tx.Value - rx.Value, 3, MidpointRounding.AwayFromZero);
                double? dbm = watts is null || watts <= 0 ? null : Math.Round(10 * Math.Log10(watts.Value * 1000), 1, MidpointRounding.AwayFromZero);
                int? azValid = az is null ? null : az >= 0 && az <= 360 ? 1 : 0;
                int? elevationValid = elevation is null ? null : elevation >= -90 && elevation <= 90 ? 1 : 0;
                Bind(emissionCommand,
                    ("$uf", uf), ("$fistel", fistel), ("$num_estacao", numEstacao), ("$num_ato", numAto),
                    ("$tecnologia", tech), ("$geracao", Generation(tech)), ("$designacao_emissao", designation),
                    ("$largura_banda_khz", Bandwidth(designation)), ("$freq_tx_mhz", tx), ("$freq_rx_mhz", rx), ("$duplex_mhz", duplex),
                    ("$azimute", az), ("$potencia_w", watts), ("$ganho_antena", gain), ("$altura_antena_m", height),
                    ("$data_licenciamento", FormatDate(licensed)), ("$data_primeiro_licenciamento", FormatDate(firstLicensed)),
                    ("$data_validade", FormatDate(validity)), ("$vencida", validity is not null && validity < Today ? 1 : 0),
                    ("$anos_licenciada", years), ("$status", Get(row, "Status.state")), ("$classe_estacao", Get(row, "CodTipoClasseEstacao")),
                    ("$polarizacao", Get(row, "Polarizacao")?.ToUpperInvariant()), ("$angulo_elevacao_graus", elevation),
                    ("$angulo_meia_potencia_graus", halfPower), ("$frente_costa_db", frontBack),
                    ("$cod_tipo_antena", Get(row, "CodTipoAntena")), ("$cod_equipamento_antena", Get(row, "CodEquipamentoAntena")),
                    ("$cod_equipamento_transmissor", Get(row, "CodEquipamentoTransmissor")), ("$potencia_dbm", dbm),
                    ("$azimute_valido", azValid), ("$elevacao_valida", elevationValid));
                emissionCommand.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    private static readonly string[] RequiredColumns = [
        "NumFistel", "NumEstacao", "SiglaUf", "NumAto", "CodMunicipio", "Municipio.NomeMunicipio", "NomeEntidade", "EnderecoEstacao",
        "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao", "FreqTxMHz", "FreqRxMHz", "Azimute", "PotenciaTransmissorWatts",
        "GanhoAntena", "AlturaAntena", "AnguloElevacao", "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao",
        "CodTipoClasseEstacao", "CodTipoAntena", "CodEquipamentoAntena", "CodEquipamentoTransmissor", "Status.state",
        "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade"];

    private static string FindRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "files")) && File.Exists(Path.Combine(dir.FullName, "src", "DDL.sql"))) return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new DirectoryNotFoundException("Não foi possível localizar a raiz com files/ e src/DDL.sql.");
    }

    private static string? Clean(string? value)
    {
        var s = value?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static double? ParseNumber(string? value)
    {
        if (value is null) return null;
        var normalized = value.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, Invariant, out var result) && double.IsFinite(result) ? result : null;
    }

    private static DateOnly? ParseDate(string? value) => value is not null && DateOnly.TryParseExact(value, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var d) ? d : null;
    private static string? FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd", Invariant);

    private static string? NormalizeText(string? value)
    {
        if (value is null) return null;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (Rune.IsLetterOrDigit(rune)) sb.Append(rune.ToString().ToUpperInvariant());
            else sb.Append(' ');
        }
        var normalized = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string Operator(string? entity)
    {
        if (entity is null) return "OUTRAS";
        if (Regex.IsMatch(entity, @"\b(TELEFONICA|VIVO)\b")) return "VIVO";
        if (Regex.IsMatch(entity, @"\b(CLARO|EMBRATEL)\b")) return "CLARO";
        if (Regex.IsMatch(entity, @"^TIM\b")) return "TIM";
        if (Regex.IsMatch(entity, @"^OI\b|\bTELEMAR\b|\bBRASIL TELECOM\b")) return "OI";
        if (Regex.IsMatch(entity, @"\bALGAR\b")) return "ALGAR";
        if (Regex.IsMatch(entity, @"\bSERCOMTEL\b")) return "SERCOMTEL";
        if (Regex.IsMatch(entity, @"\bBRISANET\b")) return "BRISANET";
        return "OUTRAS";
    }

    private static string Generation(string? technology) => technology switch
    {
        null => "N/D", "GSM" or "CDMA" => "2G", "WCDMA" or "WDCMA" => "3G", "LTE" => "4G", "NR" => "5G", _ => "OUTRA"
    };

    private static double? Bandwidth(string? designation)
    {
        if (designation is null || designation.Length < 4) return null;
        var prefix = designation[..4];
        var unitIndex = prefix.IndexOfAny(['H', 'K', 'M', 'G']);
        if (unitIndex <= 0 || prefix.IndexOfAny(['H', 'K', 'M', 'G'], unitIndex + 1) >= 0) return null;
        var whole = prefix[..unitIndex];
        var fraction = prefix[(unitIndex + 1)..];
        if (whole.Length == 0 || whole.Any(c => !char.IsAsciiDigit(c)) || fraction.Any(c => !char.IsAsciiDigit(c))) return null;
        var numberText = whole + (fraction.Length == 0 ? "" : "." + fraction);
        if (!double.TryParse(numberText, NumberStyles.AllowDecimalPoint, Invariant, out var number)) return null;
        var factor = prefix[unitIndex] switch { 'H' => 0.001, 'K' => 1d, 'M' => 1000d, 'G' => 1_000_000d, _ => 0d };
        var result = number * factor;
        return double.IsFinite(result) ? result : null;
    }

    private static SqliteCommand MakeStationCommand(SqliteConnection c, SqliteTransaction t)
    {
        var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = "INSERT INTO estacao (fistel,num_estacao,uf,cod_municipio,municipio,endereco,latitude,longitude,coordenada_valida,entidade,operadora) VALUES ($fistel,$num_estacao,$uf,$cod_municipio,$municipio,$endereco,$latitude,$longitude,$coordenada_valida,$entidade,$operadora)";
        return cmd;
    }

    private static SqliteCommand MakeEmissionCommand(SqliteConnection c, SqliteTransaction t)
    {
        var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = "INSERT INTO emissao (uf,fistel,num_estacao,num_ato,tecnologia,geracao,designacao_emissao,largura_banda_khz,freq_tx_mhz,freq_rx_mhz,duplex_mhz,azimute,potencia_w,ganho_antena,altura_antena_m,data_licenciamento,data_primeiro_licenciamento,data_validade,vencida,anos_licenciada,status,classe_estacao,polarizacao,angulo_elevacao_graus,angulo_meia_potencia_graus,frente_costa_db,cod_tipo_antena,cod_equipamento_antena,cod_equipamento_transmissor,potencia_dbm,azimute_valido,elevacao_valida) VALUES ($uf,$fistel,$num_estacao,$num_ato,$tecnologia,$geracao,$designacao_emissao,$largura_banda_khz,$freq_tx_mhz,$freq_rx_mhz,$duplex_mhz,$azimute,$potencia_w,$ganho_antena,$altura_antena_m,$data_licenciamento,$data_primeiro_licenciamento,$data_validade,$vencida,$anos_licenciada,$status,$classe_estacao,$polarizacao,$angulo_elevacao_graus,$angulo_meia_potencia_graus,$frente_costa_db,$cod_tipo_antena,$cod_equipamento_antena,$cod_equipamento_transmissor,$potencia_dbm,$azimute_valido,$elevacao_valida)";
        return cmd;
    }

    private static void Bind(SqliteCommand command, params (string Name, object? Value)[] values)
    {
        command.Parameters.Clear();
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private readonly record struct StationKey(string Uf, string Fistel, string NumEstacao);
    private readonly record struct EmissionKey(StationKey Station, string? NumAto, double? Azimute, double? Tx, double? Rx, string? Designation, string? Technology);

    private sealed class DelimitedReader : IDisposable
    {
        private readonly TextReader _reader;
        private readonly char _delimiter;
        public DelimitedReader(TextReader reader, char delimiter) { _reader = reader; _delimiter = delimiter; }
        public string[]? ReadRecord()
        {
            var fields = new List<string>(); var field = new StringBuilder(); bool quoted = false, started = false; int ch;
            while ((ch = _reader.Read()) >= 0)
            {
                var c = (char)ch; started = true;
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (_reader.Peek() == '"') { _reader.Read(); field.Append('"'); }
                        else quoted = false;
                    }
                    else field.Append(c);
                    continue;
                }
                if (c == '"' && field.Length == 0) { quoted = true; continue; }
                if (c == _delimiter) { fields.Add(field.ToString()); field.Clear(); continue; }
                if (c == '\n') { fields.Add(field.ToString()); return fields.ToArray(); }
                if (c == '\r') { if (_reader.Peek() == '\n') _reader.Read(); fields.Add(field.ToString()); return fields.ToArray(); }
                field.Append(c);
            }
            if (!started && fields.Count == 0 && field.Length == 0) return null;
            fields.Add(field.ToString());
            if (quoted) throw new InvalidDataException("Campo CSV com aspas não finalizadas.");
            return fields.ToArray();
        }
        public void Dispose() { }
    }
}
