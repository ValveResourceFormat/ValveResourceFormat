
using System.IO;
using System.Text;

namespace ValveResourceFormat.CompiledShader;

public abstract class ShaderDataBlock
{
    protected long Start { get; }

    protected ShaderDataBlock(BinaryReader datareader)
    {
        Start = datareader.BaseStream.Position;
    }

    protected ShaderDataBlock()
    {
        Start = -1;
    }

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
