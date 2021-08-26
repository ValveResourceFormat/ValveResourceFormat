using System;
using System.IO;

namespace ValveResourceFormat.ShaderParser
{
    public class ShaderDataReader : IDisposable
    {
        private BinaryReader BinReader;
        public HandleOutputWrite OutputWriter { get; set; }
        public delegate void HandleOutputWrite(string s);

        // pass an OutputWriter to direct output somewhere else, Console.Write is used by default
        public ShaderDataReader(Stream input, HandleOutputWrite OutputWriter = null)
        {
            BinReader = new BinaryReader(input);
            this.OutputWriter = OutputWriter ?? ((x) => { Console.Write(x); });
        }

#pragma warning disable CA1024 // Use properties where appropriate
        public int GetOffset()
        {
            return (int)BinReader.BaseStream.Position;
        }

        public void SetOffset(long offset)
        {
            BinReader.BaseStream.Position = offset;
        }

        public int GetFileSize()
        {
            return (int)BinReader.BaseStream.Length;
        }
#pragma warning restore CA1024

        private long savedPosition;
        public void SavePosition()
        {
            savedPosition = BinReader.BaseStream.Position;
        }

        public void RestorePosition()
        {
            BinReader.BaseStream.Position = savedPosition;
        }

        public void MoveOffset(int delta)
        {
            BinReader.BaseStream.Position += delta;
        }

        public bool CheckPositionIsAtEOF()
        {
            return BinReader.BaseStream.Position == BinReader.BaseStream.Length;
        }

        public byte ReadByte()
        {
            return BinReader.ReadByte();
        }

        public byte ReadByteAtPosition(long ind = 0, bool rel = true)
        {
            SavePosition();
            long fromInd = rel ? GetOffset() + ind : ind;
            SetOffset(fromInd);
            byte b0 = BinReader.ReadByte();
            RestorePosition();
            return b0;
        }

        public uint ReadUInt16()
        {
            return BinReader.ReadUInt16();
        }

        public uint ReadUInt16AtPosition(long ind = 0, bool rel = true)
        {
            SavePosition();
            long fromInd = rel ? GetOffset() + ind : ind;
            SetOffset(fromInd);
            uint uint0 = BinReader.ReadUInt16();
            RestorePosition();
            return uint0;
        }

        public int ReadInt16()
        {
            return BinReader.ReadInt16();
        }

        public int ReadInt16AtPosition(long ind = 0, bool rel = true)
        {
            SavePosition();
            long fromInd = rel ? GetOffset() + ind : ind;
            SetOffset(fromInd);
            short s0 = BinReader.ReadInt16();
            RestorePosition();
            return s0;
        }

        public uint ReadUInt()
        {
            return BinReader.ReadUInt32();
        }

        public uint ReadUIntAtPosition(long ind = 0, bool rel = true)
        {
            SavePosition();
            long fromInd = rel ? GetOffset() + ind : ind;
            SetOffset(fromInd);
            uint uint0 = BinReader.ReadUInt32();
            RestorePosition();
            return uint0;
        }

        public int ReadInt()
        {
            return BinReader.ReadInt32();
        }

        public int ReadIntAtPosition(long ind = 0, bool rel = true)
        {
            SavePosition();
            long fromInd = rel ? GetOffset() + ind : ind;
            SetOffset(fromInd);
            int int0 = BinReader.ReadInt32();
            RestorePosition();
            return int0;
        }

        public long ReadLong()
        {
            return BinReader.ReadInt64();
        }

        public float ReadFloat()
        {
            return BinReader.ReadSingle();
        }

        public float ReadFloatAtPosition(long ind = 0, bool rel = true)
        {
            SavePosition();
            long fromInd = rel ? GetOffset() + ind : ind;
            SetOffset(fromInd);
            BinReader.BaseStream.Position = fromInd;
            float float0 = BinReader.ReadSingle();
            RestorePosition();
            return float0;
        }

        public byte[] ReadBytes(int len)
        {
            return BinReader.ReadBytes(len);
        }

        public byte[] ReadBytesAtPosition(long ind, int len, bool rel = true)
        {
            SavePosition();
            long fromInd = rel ? GetOffset() + ind : ind;
            SetOffset(fromInd);
            byte[] bytes0 = BinReader.ReadBytes(len);
            RestorePosition();
            return bytes0;
        }
        public string ReadBytesAsString(int len)
        {
            byte[] bytes0 = BinReader.ReadBytes(len);
            return BytesToString(bytes0);
        }

        public string ReadNullTermString()
        {
            string str = "";
            byte b0 = BinReader.ReadByte();
            while (b0 > 0)
            {
                str += (char)b0;
                b0 = BinReader.ReadByte();
            }
            return str;
        }

        public string ReadNullTermStringAtPosition(long ind = 0, bool rel = true)
        {
            SavePosition();
            long fromInd = rel ? GetOffset() + ind : ind;
            SetOffset(fromInd);
            String str = ReadNullTermString();
            RestorePosition();
            return str;
        }

        public void ShowEndOfFile()
        {
            if (!CheckPositionIsAtEOF())
            {
                throw new ShaderParserException("End of file not reached");
            }
            ShowByteCount();
            OutputWriteLine("EOF");
            BreakLine();
        }

        public void ShowBytesWithIntValue()
        {
            int intval = ReadIntAtPosition();
            ShowBytes(4, breakLine: false);
            TabComment($"{intval}");
        }

        public void ShowByteCount(string message = null)
        {
            OutputWrite($"[{GetOffset()}]{(message != null ? " " + message : "")}\n");
        }

        public void ShowBytes(int len, string message = null, int tabLen = 4, bool use_slashes = true, bool breakLine = true)
        {
            ShowBytes(len, 32, message, tabLen, use_slashes, breakLine);
        }

        public void ShowBytes(int len, int breakLen, string message = null, int tabLen = 4, bool use_slashes = true, bool breakLine = true)
        {
            byte[] bytes0 = ReadBytes(len);
            string byteString = BytesToString(bytes0, breakLen);
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

        public void ShowBytesAtPosition(int ind, int len, int breakLen = 32, bool rel = true)
        {
            byte[] bytes0 = ReadBytesAtPosition(ind, len, rel);
            string bytestr = BytesToString(bytes0, breakLen);
            OutputWriteLine($"{bytestr}");
        }

        public void BreakLine()
        {
            OutputWrite("\n");
        }

        public void Comment(string message)
        {
            TabComment(message, 0, true);
        }

        public void TabComment(string message, int tabLen = 4, bool useSlashes = true)
        {
            OutputWrite($"{"".PadLeft(tabLen)}{(useSlashes ? "// " : "")}{message}\n");
        }

        public static string BytesToString(byte[] databytes, int breakLen = 32)
        {
            if (breakLen == -1)
            {
                breakLen = int.MaxValue;
            }
            int count = 0;
            string bytestring = "";
            for (int i = 0; i < databytes.Length; i++)
            {
                bytestring += $"{databytes[i]:X02} ";
                if (++count % breakLen == 0)
                {
                    bytestring += "\n";
                }
            }
            return bytestring.Trim();
        }

        public void OutputWrite(string text)
        {
            OutputWriter(text);
        }

        public void OutputWriteLine(string text)
        {
            OutputWrite(text + "\n");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && BinReader != null)
            {
                BinReader.Dispose();
                BinReader = null;
            }
        }

    }
}

