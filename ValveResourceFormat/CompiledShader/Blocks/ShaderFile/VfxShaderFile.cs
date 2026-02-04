using System.IO;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Base class for platform-specific shader bytecode.
/// </summary>
public abstract class VfxShaderFile : ShaderDataBlock
{
    /// <summary>Gets the parent static combo data.</summary>
    public VfxStaticComboData ParentCombo { get; }
    /// <summary>Gets the shader platform name.</summary>
    public abstract string BlockName { get; }
    /// <summary>Gets the shader file identifier.</summary>
    public int ShaderFileId { get; }
    /// <summary>Gets or sets the bytecode size.</summary>
    public int Size { get; protected set; }
    /// <summary>Gets or sets the shader bytecode.</summary>
    public byte[] Bytecode { get; protected set; } = [];
    /// <summary>Gets or sets the MD5 hash of the shader.</summary>
    public Guid HashMD5 { get; protected set; }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    protected VfxShaderFile(BinaryReader datareader, int sourceId, VfxStaticComboData parent) : base(datareader)
    {
        ParentCombo = parent;
        ShaderFileId = sourceId;
        Size = datareader.ReadInt32();
    }

    /// <summary>
    /// Initializes a new instance with default values.
    /// </summary>
    protected VfxShaderFile(int sourceId, VfxStaticComboData parent) : base()
    {
        ParentCombo = parent;
        ShaderFileId = sourceId;
        Size = 0;
        Bytecode = [];
        HashMD5 = Guid.Empty;
    }

    internal VfxShaderFile()
    {
        ParentCombo = null!;
        ShaderFileId = -1;
    }

    /// <summary>
    /// Decompiles the shader to source code.
    /// </summary>
    public abstract string GetDecompiledFile();

    /// <summary>
    /// Checks if the shader is empty.
    /// </summary>
    public bool IsEmpty()
    {
        return Size == 0;
    }
}
