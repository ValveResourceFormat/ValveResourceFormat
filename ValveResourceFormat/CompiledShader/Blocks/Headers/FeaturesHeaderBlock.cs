using System.Diagnostics;
using System.Text;

namespace ValveResourceFormat.CompiledShader;

public class FeaturesHeaderBlock : ShaderDataBlock
{
    public int Version { get; }
    public string FileDescription { get; }
    public int DevShader { get; }
    public int FeaturesFileFlags { get; }
    public int VertexFileFlags { get; }
    public int PixelFileFlags { get; }
    public int GeometryFileFlags { get; }
    public int HullFileFlags { get; }
    public int DomainFileFlags { get; }
    public int ComputeFileFlags { get; }
    public int[] AdditionalFileFlags { get; }
    public List<(string Name, string Shader, string StaticConfig, int Value)> Modes { get; } = [];

    public FeaturesHeaderBlock(int version, ShaderDataReader datareader, int additionalFileCount) : base(datareader)
    {
        Version = datareader.ReadInt32();

        var nameLength = datareader.ReadInt32();
        FileDescription = datareader.ReadNullTermString(Encoding.UTF8);
        UnexpectedMagicException.Assert(FileDescription.Length == nameLength, nameLength);

        // This is read as simply not being zero -- so a boolean
        DevShader = datareader.ReadInt32();

        // Valve reads all of these directly into an array based on total count of (shader types + additional files count)
        // They also appear to be just directly checking if the int is not zero, so a boolean and not flags
        // indicating which shader variants are present
        FeaturesFileFlags = datareader.ReadInt32();
        VertexFileFlags = datareader.ReadInt32();
        PixelFileFlags = datareader.ReadInt32();
        GeometryFileFlags = datareader.ReadInt32();

        if (version < 68)
        {
            HullFileFlags = datareader.ReadInt32();
            DomainFileFlags = datareader.ReadInt32();
        }

        if (version >= 63)
        {
            ComputeFileFlags = datareader.ReadInt32();
        }

        AdditionalFileFlags = new int[additionalFileCount];
        for (var i = 0; i < additionalFileCount; i++)
        {
            AdditionalFileFlags[i] = datareader.ReadInt32();
        }

        var modeCount = datareader.ReadInt32();

        for (var i = 0; i < modeCount; i++)
        {
            // CVfxMode::Unserialize
            var name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            var shader = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;

            var modeSettingsCount = datareader.ReadInt32();

            var static_config = string.Empty;
            var value = -1;
            if (modeSettingsCount > 0)
            {
                Debug.Assert(modeSettingsCount == 1); // we never supported more than 1 here

                for (var j = 0; j < modeSettingsCount; j++)
                {
                    // CVfxModeSettings::Unserialize
                    static_config = datareader.ReadNullTermStringAtPosition();
                    datareader.BaseStream.Position += 64;
                    value = datareader.ReadInt32();
                }
            }
            Modes.Add((name, shader, static_config, value));
        }
    }
}
