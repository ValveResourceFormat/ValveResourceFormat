using System.IO;

namespace ValveResourceFormat.CompiledShader;

public abstract class VfxShaderFile : ShaderDataBlock
{
    public VfxStaticComboData ParentCombo { get; }
    public abstract string BlockName { get; }
    public int ShaderFileId { get; }
    public int Size { get; protected set; }
    public byte[] Bytecode { get; protected set; } = [];
    public Guid HashMD5 { get; protected set; }

    protected VfxShaderFile(BinaryReader datareader, int sourceId, VfxStaticComboData parent) : base(datareader)
    {
        ParentCombo = parent;
        ShaderFileId = sourceId;
        Size = datareader.ReadInt32();
    }

    protected VfxShaderFile(int sourceId, VfxStaticComboData parent) : base()
    {
        ParentCombo = parent;
        ShaderFileId = sourceId;
        Size = 0;
        Bytecode = [];
        HashMD5 = Guid.Empty;
    }

    public abstract string GetDecompiledFile();

    public bool IsEmpty()
    {
        return Size == 0;
    }
}
