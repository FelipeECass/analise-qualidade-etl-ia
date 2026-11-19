using System.IO.Compression;
using System.Text;
using AnatelEtl.Models;

namespace AnatelEtl.Extract;

/// <summary>
/// Lê o CSV (separador <c>|</c>, campos entre aspas) de dentro do ZIP baixado da Anatel,
/// de forma preguiçosa, sem carregar o arquivo inteiro em memória.
/// </summary>
public static class AnatelZipReader
{
    private const char Delimiter = '|';
    private const char Quote = '"';
    private const string RequiredColumn = "NumFistel";

    public static IEnumerable<RawEmissao> Read(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Arquivo ZIP não encontrado.", zipPath);
        }

        return ReadRows(zipPath);
    }

    private static IEnumerable<RawEmissao> ReadRows(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(
                e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Nenhum CSV encontrado em '{zipPath}'.");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var headerLine = reader.ReadLine()
            ?? throw new InvalidDataException("O CSV está vazio.");

        var columns = BuildColumnIndex(SplitLine(headerLine));
        if (!columns.ContainsKey(RequiredColumn))
        {
            throw new InvalidDataException($"Coluna obrigatória '{RequiredColumn}' ausente no cabeçalho.");
        }

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            yield return MapRow(SplitLine(line), columns);
        }
    }

    /// <summary>Divide uma linha respeitando aspas e aspas duplicadas (<c>""</c>) dentro de campos.</summary>
    public static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes && c == Quote && i + 1 < line.Length && line[i + 1] == Quote)
            {
                current.Append(Quote);
                i++;
            }
            else if (c == Quote)
            {
                inQuotes = !inQuotes;
            }
            else if (c == Delimiter && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static Dictionary<string, int> BuildColumnIndex(IReadOnlyList<string> header)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            index.TryAdd(header[i].Trim(), i);
        }

        return index;
    }

    private static RawEmissao MapRow(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> columns)
    {
        string? Get(string column)
        {
            if (!columns.TryGetValue(column, out var position) || position >= fields.Count)
            {
                return null;
            }

            var value = fields[position].Trim();
            return value.Length == 0 ? null : value;
        }

        return new RawEmissao
        {
            Fistel = Get("NumFistel"),
            NumEstacao = Get("NumEstacao"),
            NumAto = Get("NumAto"),
            NomeEntidade = Get("NomeEntidade"),
            SiglaUf = Get("SiglaUf"),
            CodMunicipio = Get("CodMunicipio"),
            Municipio = Get("Municipio.NomeMunicipio"),
            Endereco = Get("EnderecoEstacao"),
            DesignacaoEmissao = Get("DesignacaoEmissao"),
            Tecnologia = Get("Tecnologia"),
            FreqTxMhz = Get("FreqTxMHz"),
            FreqRxMhz = Get("FreqRxMHz"),
            Azimute = Get("Azimute"),
            PotenciaTransmissorWatts = Get("PotenciaTransmissorWatts"),
            GanhoAntena = Get("GanhoAntena"),
            AlturaAntena = Get("AlturaAntena"),
            Latitude = Get("Latitude"),
            Longitude = Get("Longitude"),
            DataLicenciamento = Get("DataLicenciamento"),
            DataPrimeiroLicenciamento = Get("DataPrimeiroLicenciamento"),
            DataValidade = Get("DataValidade"),
            Status = Get("Status.state"),
            ClasseEstacao = Get("CodTipoClasseEstacao"),
            Polarizacao = Get("Polarizacao"),
            AnguloElevacao = Get("AnguloElevacao"),
            AnguloMeiaPotencia = Get("AnguloMeiaPotenciaAntena"),
            FrenteCosta = Get("FrenteCostaAntena"),
            CodTipoAntena = Get("CodTipoAntena"),
            CodEquipamentoAntena = Get("CodEquipamentoAntena"),
            CodEquipamentoTransmissor = Get("CodEquipamentoTransmissor"),
        };
    }
}
