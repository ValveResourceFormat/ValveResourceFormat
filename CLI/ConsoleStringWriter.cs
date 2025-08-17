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
            Console.Write(sb.ToString());
            sb.Clear();
        }
    }

    public override void Flush()
    {
        base.Flush();

        var sb = GetStringBuilder();

        if (sb.Length > 0)
        {
            Console.Write(sb.ToString());
            sb.Clear();
        }
    }
}
