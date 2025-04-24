using System.Globalization;
using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    // TODO: All these methods should be removed in favor of direct BinaryReader.Read calls.
    public class ShaderDataReader : BinaryReader
    {
        public bool IsSbox { get; init; }
        public HandleOutputWrite OutputWriter { get; set; }
        public delegate void HandleOutputWrite(string s);

        // pass an OutputWriter to direct output somewhere else, Console.Write is assigned by default
        public ShaderDataReader(Stream input, HandleOutputWrite outputWriter = null) : base(input, Encoding.UTF8, leaveOpen: true)
        {
            OutputWriter = outputWriter ?? ((x) => { Console.Write(x); });
        }

        public byte ReadByteAtPosition(long ind = 0)
        {
            var savedPosition = BaseStream.Position;
            BaseStream.Position += ind;
            var b0 = ReadByte();
            BaseStream.Position = savedPosition;
            return b0;
        }

        public uint ReadUInt16AtPosition(long ind = 0)
        {
            var savedPosition = BaseStream.Position;
            BaseStream.Position += ind;
            uint uint0 = ReadUInt16();
            BaseStream.Position = savedPosition;
            return uint0;
        }

        public int ReadInt16AtPosition(long ind = 0)
        {
            var savedPosition = BaseStream.Position;
            BaseStream.Position += ind;
            var s0 = ReadInt16();
            BaseStream.Position = savedPosition;
            return s0;
        }

        public uint ReadUInt32AtPosition(long ind = 0)
        {
            var savedPosition = BaseStream.Position;
            BaseStream.Position += ind;
            var uint0 = ReadUInt32();
            BaseStream.Position = savedPosition;
            return uint0;
        }

        public int ReadInt32AtPosition(long ind = 0)
        {
            var savedPosition = BaseStream.Position;
            BaseStream.Position += ind;
            var int0 = ReadInt32();
            BaseStream.Position = savedPosition;
            return int0;
        }

        public float ReadSingleAtPosition(long ind)
        {
            var savedPosition = BaseStream.Position;
            BaseStream.Position += ind;
            var float0 = ReadSingle();
            BaseStream.Position = savedPosition;
            return float0;
        }

        public byte[] ReadBytesAtPosition(long ind, int len)
        {
            var savedPosition = BaseStream.Position;
            BaseStream.Position += ind;
            var bytes0 = ReadBytes(len);
            BaseStream.Position = savedPosition;
            return bytes0;
        }

        public string ReadBytesAsString(int len)
        {
            var bytes0 = ReadBytes(len);
            return ShaderUtilHelpers.BytesToString(bytes0);
        }

        public string ReadNullTermStringAtPosition()
        {
            var savedPosition = BaseStream.Position;
            var str = this.ReadNullTermString(Encoding.UTF8);
            BaseStream.Position = savedPosition;
            return str;
        }

        public void ShowEndOfFile()
        {
            if (BaseStream.Position != BaseStream.Length)
            {
                throw new ShaderParserException("End of file not reached");
            }
            ShowByteCount();
            OutputWriteLine("EOF");
            BreakLine();
        }

        public void ShowBytesWithIntValue()
        {
            var intval = ReadInt32AtPosition();
            ShowBytes(4, breakLine: false);
            TabComment(intval.ToString(CultureInfo.InvariantCulture));
        }

        public void ShowByteCount(string message = null)
        {
            OutputWrite($"[{BaseStream.Position}]{(message != null ? " " + message : "")}\n");
        }

        public void ShowBytes(int len, string message = null, int tabLen = 4, bool use_slashes = true, bool breakLine = true)
        {
            ShowBytes(len, 32, message, tabLen, use_slashes, breakLine);
        }

        public void ShowBytes(int len, int breakLen, string message = null, int tabLen = 4, bool use_slashes = true, bool breakLine = true)
        {
            var bytes0 = ReadBytes(len);
            var byteString = ShaderUtilHelpers.BytesToString(bytes0, breakLen);
            OutputWrite(byteString);
            if (message != null)
            {
                TabComment(message, tabLen, use_slashes);
            }
            if (message == null && breakLine)
            {
                BreakLine();
            }
        }

        public void ShowBytesAtPosition(int ind, int len, int breakLen = 32)
        {
            var bytes0 = ReadBytesAtPosition(ind, len);
            var bytestr = ShaderUtilHelpers.BytesToString(bytes0, breakLen);
            OutputWriteLine(bytestr);
        }

        public void BreakLine()
        {
            OutputWriter("\n");
        }

        public void Comment(string message)
        {
            TabComment(message, 0, true);
        }

        public void TabComment(string message, int tabLen = 4, bool useSlashes = true)
        {
            OutputWriter($"{"".PadLeft(tabLen)}{(useSlashes ? "// " : "")}{message}\n");
        }

        public void OutputWrite(string text)
        {
            OutputWriter(text);
        }

        public void OutputWriteLine(string text)
        {
            OutputWriter(text + "\n");
        }
    }
}
