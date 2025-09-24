using System.IO;

namespace ValveResourceFormat.CompiledShader;

/*
 * The DXBC sources only have one header, the offset (which happens to be equal to their source size)
 */
public class VfxShaderFileDXBC : VfxShaderFile
{
    public override string BlockName => "DXBC";

    public VfxShaderFileDXBC(BinaryReader datareader, int sourceId, VfxStaticComboData parent) : base(datareader, sourceId, parent)
    {
        if (Size > 0)
        {
            Bytecode = datareader.ReadBytes(Size);
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }

    public override string GetDecompiledFile()
    {
        throw new InvalidOperationException("DXBC decompilation is not supported.");
    }
}
