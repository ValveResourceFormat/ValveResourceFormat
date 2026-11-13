using System.IO;

namespace CLI;

internal sealed class NullStringWriter : StringWriter
{
    public override void WriteLine()
    {
        GetStringBuilder().Clear();
    }
}
