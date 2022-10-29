
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.CompiledShader
{
    public abstract class ShaderDataBlock
    {
        public ShaderDataReader datareader { get; }
        protected long start { get; }
        protected ShaderDataBlock(ShaderDataReader datareader)
        {
            this.start = datareader.BaseStream.Position;
            this.datareader = datareader;
        }

        protected static void ThrowIfNotSupported(int vcsFileVersion)
        {
            if (vcsFileVersion != 66 && vcsFileVersion != 65 && vcsFileVersion != 64 && vcsFileVersion != 62)
            {
                throw new UnexpectedMagicException($"Unsupported version {vcsFileVersion}, versions 66, 65, 64 and 62 are supported",
                    vcsFileVersion, nameof(vcsFileVersion));
            }
        }
    }
}

