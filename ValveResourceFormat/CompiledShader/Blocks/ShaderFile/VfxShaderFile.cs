namespace ValveResourceFormat.CompiledShader;

public abstract class VfxShaderFile : ShaderDataBlock
{
    public VfxStaticComboData ParentCombo { get; }
    public abstract string BlockName { get; }
    public int SourceId { get; }
    public int Size { get; protected set; }
    public byte[] Bytecode { get; protected set; } = [];
    public Guid HashMD5 { get; protected set; }

    protected VfxShaderFile(ShaderDataReader datareader, int sourceId, VfxStaticComboData parent) : base(datareader)
    {
        ParentCombo = parent;
        SourceId = sourceId;
        Size = datareader.ReadInt32();
    }

    public bool IsEmpty()
    {
        return Size == 0;
    }
}
