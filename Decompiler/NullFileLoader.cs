using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Decompiler
{
    public class NullFileLoader : IFileLoader
    {
        public Resource LoadFile(string file) => null;
    }
}
