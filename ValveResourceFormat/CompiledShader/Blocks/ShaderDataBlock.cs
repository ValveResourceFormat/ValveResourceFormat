
namespace ValveResourceFormat.CompiledShader;

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
        const int earliest = 62;
        const int latest = 68;

        if (vcsFileVersion < earliest || vcsFileVersion > latest)
        {
            throw new UnexpectedMagicException($"Only VCS file versions {earliest} through {latest} are supported",
                vcsFileVersion, nameof(vcsFileVersion));
        }
    }
}
