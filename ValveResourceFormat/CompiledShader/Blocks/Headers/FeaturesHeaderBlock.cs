using System.Text;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.CompiledShader;

public class FeaturesHeaderBlock : ShaderDataBlock
{
    public int VcsFileVersion { get; }
    public VcsAdditionalFiles AdditionalFiles { get; }
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
    public List<(Guid, string)> EditorIDs { get; } = [];

    public int AdditionalFileCount => AdditionalFiles == VcsAdditionalFiles.PsrsAndRtx ? 2 : (int)AdditionalFiles;

    public FeaturesHeaderBlock(ShaderDataReader datareader) : base(datareader)
    {
        var vcsMagicId = datareader.ReadInt32();
        if (vcsMagicId != ShaderFile.MAGIC)
        {
            throw new UnexpectedMagicException($"Wrong magic ID, VCS expects 0x{ShaderFile.MAGIC:x}",
                vcsMagicId, nameof(vcsMagicId));
        }

        VcsFileVersion = datareader.ReadInt32();
        ThrowIfNotSupported(VcsFileVersion);

        if (VcsFileVersion >= 64)
        {
            AdditionalFiles = (VcsAdditionalFiles)datareader.ReadInt32();
        }

        if (!Enum.IsDefined(AdditionalFiles))
        {
            throw new UnexpectedMagicException("Unexpected additional files", (int)AdditionalFiles, nameof(AdditionalFiles));
        }
        else if (datareader.IsSbox && AdditionalFiles == VcsAdditionalFiles.Rtx)
        {
            datareader.BaseStream.Position += 4;
            AdditionalFiles = VcsAdditionalFiles.None;
            VcsFileVersion = 64;
        }

        Version = datareader.ReadInt32();
        datareader.BaseStream.Position += 4; // length of name, but not needed because it's always null-term
        FileDescription = datareader.ReadNullTermString(Encoding.UTF8);
        DevShader = datareader.ReadInt32();

        FeaturesFileFlags = datareader.ReadInt32();
        VertexFileFlags = datareader.ReadInt32();
        PixelFileFlags = datareader.ReadInt32();
        GeometryFileFlags = datareader.ReadInt32();

        if (VcsFileVersion < 68)
        {
            HullFileFlags = datareader.ReadInt32();
            DomainFileFlags = datareader.ReadInt32();
        }

        if (VcsFileVersion >= 63)
        {
            ComputeFileFlags = datareader.ReadInt32();
        }

        AdditionalFileFlags = new int[AdditionalFileCount];
        for (var i = 0; i < AdditionalFileCount; i++)
        {
            AdditionalFileFlags[i] = datareader.ReadInt32();
        };

        var modeCount = datareader.ReadInt32();

        for (var i = 0; i < modeCount; i++)
        {
            var name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            var shader = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;

            var static_config = string.Empty;
            var value = -1;
            if (datareader.ReadInt32() > 0)
            {
                static_config = datareader.ReadNullTermStringAtPosition();
                datareader.BaseStream.Position += 64;
                value = datareader.ReadInt32();
            }
            Modes.Add((name, shader, static_config, value));
        }

        foreach (var programType in ProgramTypeIterator())
        {
            EditorIDs.Add((new Guid(datareader.ReadBytes(16)), $"// {programType}"));
        }

        EditorIDs.Add((new Guid(datareader.ReadBytes(16)), "// Common editor/compiler hash shared by multiple different vcs files."));
    }

    public IEnumerable<VcsProgramType> ProgramTypeIterator()
    {
        var programTypeLast = (int)VcsProgramType.ComputeShader + AdditionalFileCount;

        for (var i = 0; i <= programTypeLast; i++)
        {
            var programType = (VcsProgramType)i;

            // Version 63 adds compute shaders
            if (VcsFileVersion < 63 && programType is VcsProgramType.ComputeShader)
            {
                continue;
            }

            // Version 68 removes hull and domain shaders
            if (VcsFileVersion >= 68 && programType is VcsProgramType.HullShader or VcsProgramType.DomainShader)
            {
                continue;
            }

            yield return programType;
        }
    }

    public void PrintByteDetail()
    {
        DataReader.BaseStream.Position = Start;
        DataReader.ShowByteCount("vcs file");
        DataReader.ShowBytes(4, "\"vcs2\"");
        DataReader.ShowBytes(4, $"{nameof(VcsFileVersion)} = {VcsFileVersion}");
        DataReader.BreakLine();
        DataReader.ShowByteCount("features header");
        if (VcsFileVersion >= 64)
        {
            DataReader.ShowBytes(4, $"{nameof(AdditionalFiles)} = {AdditionalFiles}");
        }
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

        var numArgs = VcsFileVersion < 64
            ? 3
            : VcsFileVersion < 68 ? 4 : 2;
        var dismissString = VcsFileVersion < 64
            ? nameof(ComputeFileFlags)
            : VcsFileVersion < 68 ? "none" : "hull & domain (v68)";
        DataReader.ShowBytes(numArgs * 4, 4, breakLine: false);
        DataReader.TabComment($"{nameof(GeometryFileFlags)}={GeometryFileFlags},{nameof(ComputeFileFlags)}={ComputeFileFlags},{nameof(HullFileFlags)}={HullFileFlags},{nameof(DomainFileFlags)}={DomainFileFlags}) dismissing: {dismissString}");

        DataReader.BreakLine();
        DataReader.ShowByteCount();

        for (var i = 0; i < (int)AdditionalFiles; i++)
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
        DataReader.ShowByteCount("Editor/Shader stack for generating the file");
        foreach (var (guid, comment) in EditorIDs)
        {
            DataReader.ShowBytes(16, comment);
        }

        DataReader.BreakLine();
    }
}
