using System.Text;

namespace AnatelEtl;

/// <summary>Streaming pipe-delimited CSV, including escaped quotes and multiline fields.</summary>
internal sealed class PipeCsvReader : IDisposable
{
    private readonly StreamReader reader;
    private readonly char[] buffer = new char[65536];
    private readonly StringBuilder value = new();
    private int position;
    private int length;
    public long RecordNumber { get; private set; }

    public PipeCsvReader(Stream stream) => reader = new StreamReader(
        stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 65536);

    private int Peek()
    {
        if (position == length)
        {
            length = reader.Read(buffer, 0, buffer.Length);
            position = 0;
        }
        return length == 0 ? -1 : buffer[position];
    }

    private int Read()
    {
        int character = Peek();
        if (character >= 0) position++;
        return character;
    }

    public string[]? ReadRecord()
    {
        if (Peek() < 0) return null;
        var fields = new List<string>(41);
        value.Clear();
        bool quoted = false, closedQuote = false;
        while (true)
        {
            int next = Read();
            if (next < 0)
            {
                if (quoted) throw Invalid("campo entre aspas não terminado");
                fields.Add(value.ToString());
                RecordNumber++;
                return fields.ToArray();
            }
            char character = (char)next;
            if (quoted)
            {
                if (character == '"')
                {
                    if (Peek() == '"') { Read(); value.Append('"'); }
                    else { quoted = false; closedQuote = true; }
                }
                else value.Append(character);
                continue;
            }
            if (character == '|' || character is '\r' or '\n')
            {
                fields.Add(value.ToString());
                value.Clear();
                closedQuote = false;
                if (character == '|') continue;
                if (character == '\r' && Peek() == '\n') Read();
                RecordNumber++;
                return fields.ToArray();
            }
            if (closedQuote)
            {
                if (!char.IsWhiteSpace(character)) throw Invalid("caractere após fechamento de aspas");
                continue;
            }
            if (character == '"')
            {
                if (!string.IsNullOrWhiteSpace(value.ToString()))
                    throw Invalid("aspas no interior de campo sem escape");
                value.Clear();
                quoted = true;
            }
            else value.Append(character);
        }
    }

    private FormatException Invalid(string message) =>
        new($"CSV inválido, registro {RecordNumber + 1}: {message}.");

    public void Dispose() => reader.Dispose();
}
