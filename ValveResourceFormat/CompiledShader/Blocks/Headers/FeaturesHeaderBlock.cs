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
        datareader.BaseStream.Position += 4; // length of name, but not needed because it's always null-term
        FileDescription = datareader.ReadNullTermString(Encoding.UTF8);
        DevShader = datareader.ReadInt32();

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

    public void PrintByteDetail(ShaderFile shaderFile)
    {
        DataReader.BaseStream.Position = Start;
        DataReader.ShowBytes(4, $"{nameof(Version)} = {Version}");
        var len_name_description = DataReader.ReadInt32AtPosition();
        DataReader.ShowBytes(4, $"{len_name_description} len of name");
        DataReader.BreakLine();
        var name_desc = DataReader.ReadNullTermStringAtPosition();
        DataReader.ShowByteCount(name_desc);
        DataReader.ShowBytes(len_name_description + 1);
        DataReader.BreakLine();
        DataReader.ShowByteCount();
        DataReader.ShowBytes(4, $"DevShader bool");
        DataReader.ShowBytes(12, 4, breakLine: false);
        DataReader.TabComment($"({nameof(FeaturesFileFlags)}={FeaturesFileFlags},{nameof(VertexFileFlags)}={VertexFileFlags},{nameof(PixelFileFlags)}={PixelFileFlags})");

        var numArgs = shaderFile.VcsVersion < 64
            ? 3
            : shaderFile.VcsVersion < 68 ? 4 : 2;
        var dismissString = shaderFile.VcsVersion < 64
            ? nameof(ComputeFileFlags)
            : shaderFile.VcsVersion < 68 ? "none" : "hull & domain (v68)";
        DataReader.ShowBytes(numArgs * 4, 4, breakLine: false);
        DataReader.TabComment($"{nameof(GeometryFileFlags)}={GeometryFileFlags},{nameof(ComputeFileFlags)}={ComputeFileFlags},{nameof(HullFileFlags)}={HullFileFlags},{nameof(DomainFileFlags)}={DomainFileFlags}) dismissing: {dismissString}");

        DataReader.BreakLine();
        DataReader.ShowByteCount();

        for (var i = 0; i < (int)shaderFile.AdditionalFileCount; i++)
        {
            DataReader.ShowBytes(4, $"arg8[{i}] = {AdditionalFileFlags[i]} (additional file {i})");
        }

        DataReader.ShowBytes(4, $"mode count = {Modes.Count}");
        DataReader.BreakLine();
        DataReader.ShowByteCount();
        foreach (var mode in Modes)
        {
            DataReader.Comment(mode.Name);
            DataReader.ShowBytes(64);
            DataReader.Comment(mode.Shader);
            DataReader.ShowBytes(64);
            DataReader.ShowBytes(4, "Has static config?");
            if (mode.StaticConfig.Length != 0)
            {
                DataReader.Comment(mode.StaticConfig);
                DataReader.ShowBytes(68);
            }
        }
        DataReader.BreakLine();
    }
}
