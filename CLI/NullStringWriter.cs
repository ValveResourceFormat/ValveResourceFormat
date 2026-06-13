using System.IO;

namespace CLI;

internal class NullStringWriter : StringWriter
{
    public override void WriteLine()
    {
        GetStringBuilder().Clear();
    }
}
