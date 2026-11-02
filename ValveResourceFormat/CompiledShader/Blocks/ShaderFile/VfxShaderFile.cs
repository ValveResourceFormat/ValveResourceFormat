using System.IO;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Base class for platform-specific shader bytecode.
/// </summary>
public abstract class VfxShaderFile : ShaderDataBlock
{
    /// <summary>Gets the parent static combo data.</summary>
    public VfxStaticComboData ParentCombo { get; }
    /// <summary>Gets the shader source format name.</summary>
    public abstract string SourceType { get; }
    /// <summary>Gets the shader file identifier.</summary>
    public int ShaderFileId { get; }
    /// <summary>Gets or sets the shader data size. Depending on the platform this may include header data in addition to the bytecode.</summary>
    public int Size { get; protected set; }
    /// <summary>Gets or sets the shader bytecode.</summary>
    public byte[] Bytecode { get; protected set; } = [];
    /// <summary>Gets or sets the MD5 hash of the shader.</summary>
    public Guid HashMD5 { get; protected set; }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    protected VfxShaderFile(BinaryReader datareader, int shaderFileId, VfxStaticComboData parent) : base(datareader)
    {
        ParentCombo = parent;
        ShaderFileId = shaderFileId;
        Size = datareader.ReadInt32();
    }

    /// <summary>
    /// Initializes a new instance with default values.
    /// </summary>
    protected VfxShaderFile(int shaderFileId, VfxStaticComboData parent) : base()
    {
        ParentCombo = parent;
        ShaderFileId = shaderFileId;
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
    /// Reads bytecode of the given size, or skips past it and returns an empty array when <paramref name="readBytecode"/> is false.
    /// </summary>
    private protected static byte[] ReadBytecode(BinaryReader datareader, int size, bool readBytecode)
    {
        if (!readBytecode)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(size);
            datareader.BaseStream.Position += size;
            return [];
        }

        return datareader.ReadBytes(size);
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
