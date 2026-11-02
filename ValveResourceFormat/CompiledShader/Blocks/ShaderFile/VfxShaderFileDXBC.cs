using System.IO;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// DirectX Bytecode (DXBC) shader file.
/// The DXBC sources only have one header, the offset (which happens to be equal to their source size).
/// </summary>
public class VfxShaderFileDXBC : VfxShaderFile
{
    /// <inheritdoc/>
    public override string SourceType => "DXBC";

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxShaderFileDXBC(BinaryReader datareader, int shaderFileId, VfxStaticComboData parent)
        : this(datareader, shaderFileId, parent, readBytecode: true)
    {
    }

    internal VfxShaderFileDXBC(BinaryReader datareader, int shaderFileId, VfxStaticComboData parent, bool readBytecode)
        : base(datareader, shaderFileId, parent)
    {
        if (Size > 0)
        {
            Bytecode = ReadBytecode(datareader, Size, readBytecode);
        }

        HashMD5 = datareader.ReadGuid();
    }

    /// <summary>
    /// Initializes a new instance with explicit size and hash.
    /// </summary>
    public VfxShaderFileDXBC(BinaryReader datareader, int shaderFileId, int size, Guid hash, VfxStaticComboData parent)
        : this(datareader, shaderFileId, size, hash, parent, readBytecode: true)
    {
    }

    internal VfxShaderFileDXBC(BinaryReader datareader, int shaderFileId, int size, Guid hash, VfxStaticComboData parent, bool readBytecode)
        : base(shaderFileId, parent)
    {
        Size = size;
        Bytecode = ReadBytecode(datareader, size, readBytecode);
        HashMD5 = hash;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// DXBC decompilation is not supported. Showing hash for now is useful for finding shader from renderdoc.
    /// </remarks>
    public override string GetDecompiledFile()
    {
        return $"Shader hash: {new Guid(Bytecode.AsSpan(4, 16))}\n\nDXBC decompilation is currently not supported.";
    }
}
