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
        private string ShaderPlatform;
        private uint version;

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
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

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

            if (filename.Contains("vulkan"))
            {
                ShaderPlatform = "vulkan";
            }
            else if (filename.Contains("pcgl"))
            {
                ShaderPlatform = "opengl";
            }
            else if (filename.Contains("pc_"))
            {
                ShaderPlatform = "directx";
            }

            Reader = new BinaryReader(input);

            if (Reader.ReadUInt32() != MAGIC)
            {
                throw new InvalidDataException("Given file is not a vcs2.");
            }

            // This is static across all files, is it version?
            // Known versions:
            //  62 - April 2016
            //  63 - March 2017
            //  64 - May 2017
            version = Reader.ReadUInt32();

            if (version < 62 || version > 64)
            {
                throw new InvalidDataException("Unsupported VCS2 version: " + version);
            }

            if (version >= 64)
            {
                Reader.ReadUInt32(); // always 0, new in version 64
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

            if (version >= 63)
            {
                var h = Reader.ReadInt32();
                Console.WriteLine($"{a} {b} {c} {d} {e} {f} {g} {h}");
            }
            else
            {
                Console.WriteLine($"{a} {b} {c} {d} {e} {f} {g}");
            }

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

            var identifierCount = 7;

            if (version > 62)
            {
                identifierCount = 8;
            }

            // Appears to be always 128 bytes in version 63 and higher, 112 before
            for (var i = 0; i < identifierCount; i++)
            {
                // 0 - ?
                // 1 - vertex shader
                // 2 - pixel shader
                // 3 - geometry shader
                // 4 - hull shader
                // 5 - domain shader
                // 6 - ?
                // 7 - ?, new in version 63 
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

                if (version > 62)
                {
                    Reader.BaseStream.Position += 4;
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
                Console.WriteLine("Extracting shader " + i + "..");
                // File.WriteAllBytes(Path.Combine(@"D:\shaders\PCGL DotA Core\processed spritecard\", "shader_out_" + i + ".bin"), ReadShaderChunk(lzmaOffsets[i]));

                // Skip non-PCGL shaders for now, need to figure out platform without checking filename
                if (ShaderPlatform != "opengl")
                {
                    continue;
                }

                // What follows here is super experimental and barely works as is. It is a very rough implementation to read and extract shader stringblocks for PCGL shaders.
                using (var inputStream = new MemoryStream(ReadShaderChunk(lzmaOffsets[i])))
                using (var chunkReader = new BinaryReader(inputStream))
                {
                    while (chunkReader.BaseStream.Position < chunkReader.BaseStream.Length)
                    {
                        // Read count that also doubles as mode?
                        var modeAndCount = chunkReader.ReadInt16();

                        // Mode never seems to be 20 for anything but the FF chunk before shader stringblock
                        if (modeAndCount != 20)
                        {
                            chunkReader.ReadInt16();
                            var unk2 = chunkReader.ReadInt32();
                            var unk3 = chunkReader.ReadInt32();

                            // If the mode isn't the same as unk3, skip shader for now
                            if (modeAndCount != unk3)
                            {
                                Console.WriteLine("Having issues reading shader " + i + ", skipping..");
                                chunkReader.BaseStream.Position = chunkReader.BaseStream.Length;
                                continue;
                            }

                            chunkReader.ReadBytes(unk3 * 4);

                            var unk4 = chunkReader.ReadUInt16();

                            // Seems to be 1 if there's a string there, read 26 byte stringblock, roll back if not
                            if (unk4 == 1)
                            {
                                chunkReader.ReadBytes(26);
                            }
                            else
                            {
                                chunkReader.BaseStream.Position -= 2;
                            }
                        }
                        else if (modeAndCount == 20)
                        {
                            // Read 40 byte 0xFF chunk
                            chunkReader.ReadBytes(40);

                            // Read 5 unknown bytes
                            chunkReader.ReadBytes(5);

                            // Shader stringblock count
                            var shaderContentCount = chunkReader.ReadUInt32();

                            // Read trailing byte
                            chunkReader.ReadByte();

                            // If shader stringblock count is ridiculously high stop reading this shader and bail
                            if (shaderContentCount > 100)
                            {
                                Console.WriteLine("Having issues reading shader " + i + ", skipping..");
                                chunkReader.BaseStream.Position = chunkReader.BaseStream.Length;
                                continue;
                            }

                            // Read and dump all shader stringblocks
                            for (int j = 0; j < shaderContentCount; j++)
                            {
                                var shaderLengthInclHeader = chunkReader.ReadInt32();
                                var unk = chunkReader.ReadUInt32(); //type?
                                Console.WriteLine(unk);
                                var shaderContentLength = chunkReader.ReadInt32();
                                var shaderContent = chunkReader.ReadChars(shaderContentLength);

                                // File.WriteAllText(Path.Combine(@"D:\shaders\PCGL DotA Core\processed spritecard", "shader_out_" + i + "_" + j + ".txt"), new string(shaderContent));
                                var shaderContentChecksum = chunkReader.ReadBytes(16);
                            }

                            // Reached end of shader content, skip remaining file length
                            chunkReader.ReadBytes((int)chunkReader.BaseStream.Length - (int)chunkReader.BaseStream.Position);
                        }
                    }
                }
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
