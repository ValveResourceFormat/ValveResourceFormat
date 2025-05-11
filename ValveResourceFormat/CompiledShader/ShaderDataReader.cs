using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    // TODO: All these methods should be removed in favor of direct BinaryReader.Read calls.
    public class ShaderDataReader : BinaryReader
    {
        public delegate void HandleOutputWrite(string s);

        public ShaderDataReader(Stream input) : base(input, Encoding.UTF8, leaveOpen: true)
        {
            //
        }

        public int ReadInt32AtPosition(long ind = 0)
        {
            var savedPosition = BaseStream.Position;
            BaseStream.Position += ind;
            var int0 = ReadInt32();
            BaseStream.Position = savedPosition;
            return int0;
        }

        public string ReadNullTermStringAtPosition()
        {
            var savedPosition = BaseStream.Position;
            var str = this.ReadNullTermString(Encoding.UTF8);
            BaseStream.Position = savedPosition;
            return str;
        }
    }
}
