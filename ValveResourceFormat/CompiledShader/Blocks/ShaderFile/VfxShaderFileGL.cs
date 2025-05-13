using System.IO;

namespace ValveResourceFormat.CompiledShader;

public class VfxShaderFileGL : VfxShaderFile
{
    public override string BlockName => "GLSL";
    public int Arg0 { get; } // always 3
    // offset2, if present, always observes offset2 == offset + 8
    // offset2 can also be interpreted as the source-size
    public int BytecodeSize { get; } = -1;

    public VfxShaderFileGL(BinaryReader datareader, int sourceId, VfxStaticComboData parent) : base(datareader, sourceId, parent)
    {
        if (Size > 0)
        {
            Arg0 = datareader.ReadInt32();
            BytecodeSize = datareader.ReadInt32();
            Bytecode = datareader.ReadBytes(BytecodeSize - 1); // -1 because the sourcebytes are null-term
            datareader.BaseStream.Position += 1;
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }
}
