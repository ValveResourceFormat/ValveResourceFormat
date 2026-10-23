using System.IO;
using System.Text;

namespace CLI;

internal class ConsoleStringWriter : StringWriter
{
    const int MaxLength = 64 * 1024 * 1024;

    public ConsoleStringWriter(StringBuilder sb, IFormatProvider? formatProvider)
        : base(sb, formatProvider)
    {
    }

    public override void WriteLine()
    {
        base.WriteLine();

        var sb = GetStringBuilder();

        if (sb.Length >= MaxLength)
        {
            WriteChunks(sb);
            sb.Clear();
        }
    }

    public override void Flush()
    {
        base.Flush();

        var sb = GetStringBuilder();

        if (sb.Length > 0)
        {
            WriteChunks(sb);
            sb.Clear();
        }
    }

    private static void WriteChunks(StringBuilder sb)
    {
        foreach (var chunk in sb.GetChunks())
        {
            Console.Out.Write(chunk.Span);
        }
    }
}
