using System;
using System.IO;
using System.Text;
using Decoder = SevenZip.Compression.LZMA.Decoder;

namespace ValveResourceFormat
{
    public class CompiledShader : IDisposable
    {
        public const int MAGIC = 0x32736376; // "vcs2"

        private BinaryReader Reader;
        private string ShaderType;

        /// <summary>
        /// Releases binary reader.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && Reader != null)
            {
                Reader.Dispose();
                Reader = null;
            }
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);

            Read(filename, fs);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="filename">The filename <see cref="string"/>.</param>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(string filename, Stream input)
        {
            // TODO: Does Valve really have separate parsing for different shader files? See vertex buffer only string chunk below.
            if (filename.EndsWith("vs.vcs"))
            {
                ShaderType = "vertex";
            }
            else if (filename.EndsWith("ps.vcs"))
            {
                ShaderType = "pixel";
            }
            else if (filename.EndsWith("features.vcs"))
            {
                ShaderType = "features";
            }

            Reader = new BinaryReader(input);

            if (Reader.ReadUInt32() != MAGIC)
            {
                throw new InvalidDataException("Given file is not a vcs2.");
            }

            // This is static across all files, is it version?
            if (Reader.ReadUInt32() != 0x0000003E)
            {
                throw new InvalidDataException("Not 3E.");
            }

            var wtf = Reader.ReadUInt32();

            // TODO: Odd assumption, features.vcs seems to have this
            if (wtf < 100)
            {
                Console.WriteLine("wtf: {0}", wtf);
                ReadFeatures();
            }
            else
            {
                Reader.BaseStream.Position -= 4;
                ReadShader();
            }
        }

        private void ReadFeatures()
        {
            var name = Encoding.UTF8.GetString(Reader.ReadBytes(Reader.ReadInt32()));
            Reader.ReadByte(); // null term?

            Console.WriteLine("Name: {0} - Offset: {1}", name, Reader.BaseStream.Position);

            var a = Reader.ReadInt32();
            var b = Reader.ReadInt32();
            var c = Reader.ReadInt32();
            var d = Reader.ReadInt32();
            var e = Reader.ReadInt32();
            var f = Reader.ReadInt32();
            var g = Reader.ReadInt32();

            Console.WriteLine($"{a} {b} {c} {d} {e} {f} {g}");

            var count = Reader.ReadUInt32();

            long prevPos;

            Console.WriteLine("Count: {0}", count);

            for (var i = 0; i < count; i++)
            {
                prevPos = Reader.BaseStream.Position;

                name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = prevPos + 128;

                var type = Reader.ReadUInt32();

                Console.WriteLine("Name: {0} - Type: {1} - Offset: {2}", name, type, Reader.BaseStream.Position);

                if (type == 1)
                {
                    prevPos = Reader.BaseStream.Position;

                    var subname = Reader.ReadNullTermString(Encoding.UTF8);

                    Console.WriteLine(subname);
                    Reader.BaseStream.Position = prevPos + 64;

                    Reader.ReadUInt32();
                }
            }

            // Appears to be always 112 bytes
            for (var i = 0; i < 7; i++)
            {
                // 0 -
                // 1 - vertex shader
                // 2 - pixel shader
                // 3 - geometry shader
                // 4 - hull shader
                // 5 - domain shader
                // 6 - CD B1 D4.... hardcoded, this one might be 20 bytes because (0E 00 00 00) appears in other files too
                var identifier = Reader.ReadBytes(16);

                Console.WriteLine("#{0} identifier: {1}", i, BitConverter.ToString(identifier));
            }

            Reader.ReadUInt32(); // 0E 00 00 00

            count = Reader.ReadUInt32();

            for (var i = 0; i < count; i++)
            {
                prevPos = Reader.BaseStream.Position;

                name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = prevPos + 64;

                prevPos = Reader.BaseStream.Position;

                var desc = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = prevPos + 84;

                var subcount = Reader.ReadUInt32();

                Console.WriteLine("Name: {0} - Desc: {1} - Count: {2} - Offset: {3}", name, desc, subcount, Reader.BaseStream.Position);

                for (var j = 0; j < subcount; j++)
                {
                    Console.WriteLine("     " + Reader.ReadNullTermString(Encoding.UTF8));
                }
            }

            count = Reader.ReadUInt32();
        }

        private void ReadShader()
        {
            var fileIdentifier = Reader.ReadBytes(16);

            // This appears to always be CD B1 D4 A1 82 20 D5 E2 D5 A3 78 2E C8 0F D7 7C
            // Including aperture robot repair and dota 2
            var staticIdentifier = Reader.ReadBytes(16);

            Console.WriteLine("File identifier: {0}", BitConverter.ToString(fileIdentifier));
            Console.WriteLine("Static identifier: {0}", BitConverter.ToString(staticIdentifier));

            Console.WriteLine("wtf {0}", Reader.ReadUInt32());

            // 2
            var count = Reader.ReadUInt32();

            Console.WriteLine("Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var previousPosition = Reader.BaseStream.Position;

                var name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = previousPosition + 128;

                var a = Reader.ReadInt32();
                var b = Reader.ReadInt32();
                var c = Reader.ReadInt32();
                var d = Reader.ReadInt32();
                var e = Reader.ReadInt32();
                var f = Reader.ReadInt32();

                Console.WriteLine($"{a} {b} {c} {d} {e} {f} {name}");
            }

            // 3
            count = Reader.ReadUInt32();

            Console.WriteLine("Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                Reader.BaseStream.Position += 118 * 4;
            }

            // 4
            count = Reader.ReadUInt32();

            Console.WriteLine("Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var previousPosition = Reader.BaseStream.Position;

                var name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = previousPosition + 128;

                var a = Reader.ReadInt32();
                var b = Reader.ReadInt32();
                var c = Reader.ReadInt32();
                var d = Reader.ReadInt32();
                var e = Reader.ReadInt32();
                var f = Reader.ReadInt32();

                Console.WriteLine($"{a} {b} {c} {d} {e} {f} {name}");
            }

            // 5
            count = Reader.ReadUInt32();

            Console.WriteLine("Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                Reader.BaseStream.Position += 118 * 4;
            }

            // 6
            count = Reader.ReadUInt32();

            Console.WriteLine("Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var previousPosition = Reader.BaseStream.Position;

                var name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = previousPosition + 200; // ??

                var type = Reader.ReadInt32();
                var b = Reader.ReadInt32();

                if (b > -1 && type != 0)
                {
                    Reader.BaseStream.Position = previousPosition + 480 + b + 4;
                }
                else
                {
                    Reader.BaseStream.Position = previousPosition + 480;
                }

                Console.WriteLine($"{type} {b} {name}");
            }

            // 7
            count = Reader.ReadUInt32();

            Console.WriteLine("Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            Reader.ReadBytes(280 * (int)count); // ?

            // 8
            count = Reader.ReadUInt32();

            Console.WriteLine("Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var prevPos = Reader.BaseStream.Position;

                var name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = prevPos + 64;

                var a = Reader.ReadUInt32();
                var b = Reader.ReadUInt32();

                var subCount = Reader.ReadUInt32();

                Console.WriteLine("[SUB CHUNK] Name: {0} - unk1: {1} - unk2: {2} - Count: {3} - Offset: {4}", name, a, b, subCount, Reader.BaseStream.Position);

                for (var j = 0; j < subCount; j++)
                {
                    var previousPosition = Reader.BaseStream.Position;

                    var subname = Reader.ReadNullTermString(Encoding.UTF8);

                    Reader.BaseStream.Position = previousPosition + 64;

                    var unk1 = Reader.ReadUInt32();
                    var unk2 = Reader.ReadUInt32();
                    var unk3 = Reader.ReadUInt32();
                    var unk4 = Reader.ReadUInt32();

                    Console.WriteLine("     Name: {0} - unk1: {1} - unk2: {2} - unk3: {3} - unk4: {4}", subname, unk1, unk2, unk3, unk4);
                }

                Reader.ReadBytes(4); // ?
            }

            Console.WriteLine("Offset: {0}", Reader.BaseStream.Position);

            // Vertex shader has a string chunk which seems to be vertex buffer specifications
            if (ShaderType == "vertex")
            {
                var bufferCount = Reader.ReadUInt32();

                Console.WriteLine(bufferCount + " vertex buffer descriptors");
                for (var h = 0; h < bufferCount; h++)
                {
                    count = Reader.ReadUInt32(); // number of attributes

                    Console.WriteLine("Buffer #{0}, {1} attributes", h, count);

                    for (var i = 0; i < count; i++)
                    {
                        var name = Reader.ReadNullTermString(Encoding.UTF8);
                        var type = Reader.ReadNullTermString(Encoding.UTF8);
                        var option = Reader.ReadNullTermString(Encoding.UTF8);
                        var unk = Reader.ReadUInt32(); // 0, 1, 2, 13 or 14

                        Console.WriteLine("     Name: {0}, Type: {1}, Option: {2}, Unknown uint: {3}", name, type, option, unk);
                    }
                }
            }

            var lzmaCount = Reader.ReadUInt32();

            Console.WriteLine("Offset: {0}", Reader.BaseStream.Position);

            var unkLongs = new long[lzmaCount];
            for (var i = 0; i < lzmaCount; i++)
            {
                unkLongs[i] = Reader.ReadInt64();
            }

            var lzmaOffsets = new int[lzmaCount];

            for (var i = 0; i < lzmaCount; i++)
            {
                lzmaOffsets[i] = Reader.ReadInt32();
            }

            for (var i = 0; i < lzmaCount; i++)
            {
                //File.WriteAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "shader_out_" + i + ".bin"), ReadShaderChunk(lzmaOffsets[i]));
            }
        }

        private byte[] ReadShaderChunk(int offset)
        {
            var prevPos = Reader.BaseStream.Position;

            Reader.BaseStream.Position = offset;

            var chunkSize = Reader.ReadUInt32();

            if (Reader.ReadUInt32() != 0x414D5A4C)
            {
                throw new InvalidDataException("Not LZMA?");
            }

            var uncompressedSize = Reader.ReadUInt32();
            var compressedSize = Reader.ReadUInt32();

            Console.WriteLine("Chunk size: {0}", chunkSize);
            Console.WriteLine("Compressed size: {0}", compressedSize);
            Console.WriteLine("Uncompressed size: {0} ({1:P2} compression)", uncompressedSize, (uncompressedSize - compressedSize) / (double)uncompressedSize);

            var decoder = new Decoder();
            decoder.SetDecoderProperties(Reader.ReadBytes(5));

            var compressedBuffer = Reader.ReadBytes((int)compressedSize);

            Reader.BaseStream.Position = prevPos;

            using (var inputStream = new MemoryStream(compressedBuffer))
            using (var outStream = new MemoryStream((int)uncompressedSize))
            {
                decoder.Code(inputStream, outStream, compressedBuffer.Length, uncompressedSize, null);

                return outStream.ToArray();
            }
        }
    }
}
