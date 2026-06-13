using System.IO;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// DirectX Bytecode (DXBC) shader file.
/// The DXBC sources only have one header, the offset (which happens to be equal to their source size).
/// </summary>
public class VfxShaderFileDXBC : VfxShaderFile
{
    /// <inheritdoc/>
    public override string BlockName => "DXBC";

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxShaderFileDXBC(BinaryReader datareader, int sourceId, VfxStaticComboData parent)
        : base(datareader, sourceId, parent)
    {
        if (Size > 0)
        {
            Bytecode = datareader.ReadBytes(Size);
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }

    /// <summary>
    /// Initializes a new instance with explicit size and hash.
    /// </summary>
    public VfxShaderFileDXBC(BinaryReader datareader, int sourceId, int size, Guid hash, VfxStaticComboData parent)
        : base(sourceId, parent)
    {
        Size = size;
        Bytecode = datareader.ReadBytes(size);
        HashMD5 = hash;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// DXBC decompilation is not supported. Showing hash for now is useful for finding shader from renderdoc.
    /// </remarks>
    public override string GetDecompiledFile()
    {
        return $"Shader hash: {new Guid(Bytecode.AsSpan(4, 16)).ToString()}\n\nDXBC decompilation is currently not supported.";
    }
}
