using System.IO.Compression;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace AnatelEtl;

internal sealed class CsvSource : IDisposable
{
    private readonly ZipArchive archive;
    private readonly TextFieldParser parser;
    private readonly Dictionary<string, int> columns;
    private readonly int width;
    internal static readonly string[] Required =
    [
        "NumFistel", "NumEstacao", "NumAto", "SiglaUf", "CodMunicipio", "Municipio.NomeMunicipio",
        "NomeEntidade", "EnderecoEstacao", "Latitude", "Longitude", "Tecnologia", "DesignacaoEmissao",
        "FreqTxMHz", "FreqRxMHz", "Azimute", "PotenciaTransmissorWatts", "GanhoAntena", "AlturaAntena",
        "AnguloElevacao", "AnguloMeiaPotenciaAntena", "FrenteCostaAntena", "Polarizacao", "CodTipoClasseEstacao",
        "CodTipoAntena", "CodEquipamentoAntena", "CodEquipamentoTransmissor", "Status.state",
        "DataLicenciamento", "DataPrimeiroLicenciamento", "DataValidade"
    ];
    internal CsvSource(string path)
    {
        archive = ZipFile.OpenRead(path);
        try
        {
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
            if (entries.Length != 1 || !entries[0].Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{path}: esperado um único arquivo CSV.");
            parser = new TextFieldParser(entries[0].Open(), new UTF8Encoding(false, true), true)
            {
                TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
            };
            parser.SetDelimiters("|");
            var header = parser.ReadFields() ?? throw new InvalidDataException($"{path}: sem cabeçalho.");
            width = header.Length;
            columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < header.Length; i++)
                if (!columns.TryAdd(header[i].Trim(), i)) throw new InvalidDataException($"Cabeçalho duplicado: {header[i]}.");
            foreach (var column in Required)
                if (!columns.ContainsKey(column)) throw new InvalidDataException($"{path}: coluna ausente: {column}.");
        }
        catch { archive.Dispose(); throw; }
    }
    internal string?[]? Read()
    {
        if (parser.EndOfData) return null;
        var row = parser.ReadFields();
        if (row is null) return null;
        if (row.Length != width) throw new InvalidDataException($"Registro com {row.Length} campos; esperado {width}; linha {parser.LineNumber}.");
        for (int i = 0; i < row.Length; i++) row[i] = Transform.Clean(row[i])!;
        return row;
    }
    internal string? Get(string?[] row, string name) => row[columns[name]];
    public void Dispose() { parser.Dispose(); archive.Dispose(); }
}
