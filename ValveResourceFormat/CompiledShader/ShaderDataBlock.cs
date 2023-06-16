
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.CompiledShader
{
    public abstract class ShaderDataBlock
    {
        public ShaderDataReader DataReader { get; }
        protected long Start { get; }
        protected ShaderDataBlock(ShaderDataReader datareader)
        {
            Start = datareader.BaseStream.Position;
            DataReader = datareader;
        }

        protected static void ThrowIfNotSupported(int vcsFileVersion)
        {
            if (vcsFileVersion != 62 && (vcsFileVersion < 64 || vcsFileVersion > 68))
            {
                throw new UnexpectedMagicException($"Unsupported shader version, versions 67, 66, 65, 64 and 62 are supported",
                    vcsFileVersion, nameof(vcsFileVersion));
            }
        }
    }
}

