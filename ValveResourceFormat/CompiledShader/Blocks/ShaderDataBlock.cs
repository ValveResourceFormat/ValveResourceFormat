
using System.IO;
using System.Text;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Base class for shader data blocks.
/// </summary>
public abstract class ShaderDataBlock
{
    /// <summary>Gets the starting position in the stream.</summary>
    protected long Start { get; }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    protected ShaderDataBlock(BinaryReader datareader)
    {
        Start = datareader.BaseStream.Position;
    }

    /// <summary>
    /// Initializes a new instance with default values.
    /// </summary>
    protected ShaderDataBlock()
    {
        Start = -1;
    }

    /// <summary>
    /// Reads a null-terminated string with a fixed maximum length.
    /// </summary>
    public static string ReadStringWithMaxLength(BinaryReader datareader, int length)
    {
        var str = datareader.ReadNullTermString(Encoding.UTF8);
        var remainder = length - str.Length - 1;

        if (remainder < 0)
        {
            throw new InvalidDataException("Read string was longer than expected");
        }

        datareader.BaseStream.Position += remainder;
        return str;
    }
}
