using System.IO;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// DirectX Intermediate Language (DXIL) shader file.
/// </summary>
public class VfxShaderFileDXIL : VfxShaderFile
{
    /// <inheritdoc/>
    public override string SourceType => "DXIL";
    /// <summary>Gets the shader version, packed as major &lt;&lt; 8 | minor.</summary>
    public int Version { get; }
    /// <summary>Gets the shader type token: 0xFFFF for pixel shaders, 0xFFFE for vertex shaders.</summary>
    public int ShaderTypeToken { get; }
    /// <summary>Gets the size of the constant table comment block in bytes.</summary>
    public int HeaderBytes { get; }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxShaderFileDXIL(BinaryReader datareader, int shaderFileId, VfxStaticComboData parent) : base(datareader, shaderFileId, parent)
    {
        if (Size > 0)
        {
            Version = datareader.ReadInt16();
            ShaderTypeToken = (int)datareader.ReadUInt16();
            uint delimiter = datareader.ReadUInt16();
            if (delimiter != 0xFFFE)
            {
                throw new ShaderParserException($"Unexpected DXIL delimiter {delimiter:x8}");
            }

            HeaderBytes = (int)datareader.ReadUInt16() * 4; // size is given as a 4-byte count
            Bytecode = datareader.ReadBytes(Size - 8);
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// DXIL decompilation is not supported. This method always throws an InvalidOperationException.
    /// </remarks>
    public override string GetDecompiledFile()
    {
        throw new InvalidOperationException("DXIL decompilation is not supported.");
    }
}
