
namespace ValveResourceFormat.CompiledShader;

public class VsPsHeaderBlock : ShaderDataBlock
{
    public int VcsFileVersion { get; }
    public Guid FileID0 { get; }
    public Guid FileID1 { get; }
    public VsPsHeaderBlock(ShaderDataReader datareader) : base(datareader)
    {
        var vcsMagicId = datareader.ReadInt32();
        if (vcsMagicId != ShaderFile.MAGIC)
        {
            throw new UnexpectedMagicException($"Wrong magic ID, VCS expects 0x{ShaderFile.MAGIC:x}",
                vcsMagicId, nameof(vcsMagicId));
        }

        VcsFileVersion = datareader.ReadInt32();
        ThrowIfNotSupported(VcsFileVersion);

        var extraFile = VcsAdditionalFiles.None;
        if (VcsFileVersion >= 64)
        {
            extraFile = (VcsAdditionalFiles)datareader.ReadInt32();
            if (extraFile < VcsAdditionalFiles.None || extraFile > VcsAdditionalFiles.PsrsAndRtx)
            {
                throw new UnexpectedMagicException("unexpected v64 value", (int)extraFile, nameof(VcsAdditionalFiles));
            }
            if (datareader.IsSbox && extraFile == VcsAdditionalFiles.Rtx)
            {
                datareader.BaseStream.Position += 4;
                VcsFileVersion--;
            }
        }
        FileID0 = new Guid(datareader.ReadBytes(16));
        FileID1 = new Guid(datareader.ReadBytes(16));
    }

    public void PrintByteDetail()
    {
        DataReader.BaseStream.Position = Start;
        DataReader.ShowByteCount("vcs file");
        DataReader.ShowBytes(4, "\"vcs2\"");
        var vcs_version = DataReader.ReadInt32AtPosition();
        DataReader.ShowBytes(4, $"version {vcs_version}");
        DataReader.BreakLine();
        DataReader.ShowByteCount("ps/vs header");
        if (vcs_version >= 64)
        {
            var has_psrs_file = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"has_psrs_file = {(has_psrs_file > 0 ? "True" : "False")}");
        }
        DataReader.BreakLine();
        DataReader.ShowByteCount("Editor/Shader stack for generating the file");
        DataReader.ShowBytes(16, "Editor ref. ID0 (produces this file)");
        DataReader.ShowBytes(16, "Common editor/compiler hash shared by multiple different vcs files.");
    }
}
