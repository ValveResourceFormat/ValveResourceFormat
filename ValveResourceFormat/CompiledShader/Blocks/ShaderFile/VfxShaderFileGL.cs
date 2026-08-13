using System.IO;
using System.Text;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// OpenGL GLSL shader file.
/// </summary>
public class VfxShaderFileGL : VfxShaderFile
{
    /// <inheritdoc/>
    public override string BlockName => "GLSL";

    /// <summary>Gets the shader file version. It is 2 for PCGL and 3 for MOBILE_GLES.</summary>
    public int Version { get; }

    /// <summary>Gets the shader source code size.</summary>
    public int BytecodeSize { get; } = -1;

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxShaderFileGL(BinaryReader datareader, int sourceId, VfxStaticComboData parent) : base(datareader, sourceId, parent)
    {
        if (Size > 0)
        {
            Version = datareader.ReadInt32();
            BytecodeSize = datareader.ReadInt32();
            Bytecode = datareader.ReadBytes(BytecodeSize - 1); // -1 because the sourcebytes are null-term
            datareader.BaseStream.Position += 1;
        }

        HashMD5 = new Guid(datareader.ReadBytes(16));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the GLSL shader source code as a UTF-8 string.
    /// </remarks>
    public override string GetDecompiledFile()
    {
        return Encoding.UTF8.GetString(this.Bytecode);
    }
}
