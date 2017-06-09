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

            // Known versions:
            //  62 - April 2016
            //  63 - March 2017
            //  64 - May 2017
            var version = Reader.ReadUInt32();

            if (version != 64)
            {
                throw new InvalidDataException("Unsupported VCS2 version: " + version);
            }

            if (ShaderType == "features")
            {
                ReadFeatures();
            }
            else
            {
                ReadShader();
            }
        }

        private void ReadFeatures()
        {
            var anotherFileRef = Reader.ReadInt32(); // new in version 64, mostly 0 but sometimes 1

            var wtf = Reader.ReadUInt32(); // appears to be 0 in 'features'
            Console.WriteLine("wtf: {0}", wtf);

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
            var h = Reader.ReadInt32();
            if (anotherFileRef == 1)
            {
                var i = Reader.ReadInt32();
                Console.WriteLine($"{a} {b} {c} {d} {e} {f} {g} {h} {i}");
            }
            else
            {
                Console.WriteLine($"{a} {b} {c} {d} {e} {f} {g} {h}");
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

            var identifierCount = 8;

            if (anotherFileRef == 1)
            {
                identifierCount++;
            }

            // Appears to be always 128 bytes in version 63 and higher, 112 before
            for (var i = 0; i < identifierCount; i++)
            {
                // either 6 or 7 is cs (compute shader)
                // 0 - ?
                // 1 - vertex shader
                // 2 - pixel shader
                // 3 - geometry shader
                // 4 - hull shader
                // 5 - domain shader
                // 6 - ?
                // 7 - ?, new in version 63
                // 8 - pixel shader render state (only if uint in version 64+ at pos 8 is 1)
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
            // This uint controls whether or not there's an additional uint and file identifier in header for features shader, might be something different in these.
            var unk0_a = Reader.ReadInt32(); // new in version 64, mostly 0 but sometimes 1

            var fileIdentifier = Reader.ReadBytes(16);
            var staticIdentifier = Reader.ReadBytes(16);

            Console.WriteLine("File identifier: {0}", BitConverter.ToString(fileIdentifier));
            Console.WriteLine("Static identifier: {0}", BitConverter.ToString(staticIdentifier));

            var unk0_b = Reader.ReadUInt32();
            Console.WriteLine("wtf {0}", unk0_b); // Always 14?

            // Chunk 1
            var count = Reader.ReadUInt32();

            Console.WriteLine("[CHUNK 1] Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var previousPosition = Reader.BaseStream.Position;

                var name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = previousPosition + 128;

                var unk1_a = Reader.ReadInt32();
                var unk1_b = Reader.ReadInt32();
                var unk1_c = Reader.ReadInt32();
                var unk1_d = Reader.ReadInt32();
                var unk1_e = Reader.ReadInt32();
                var unk1_f = Reader.ReadInt32();

                Console.WriteLine($"{unk1_a} {unk1_b} {unk1_c} {unk1_d} {unk1_e} {unk1_f} {name}");
            }

            // Chunk 2 - Similar structure to chunk 4, same chunk size
            count = Reader.ReadUInt32();

            Console.WriteLine("[CHUNK 2] Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                // Initial research based on brushsplat_pc_40_ps, might be different for other shaders
                var unk2_a = Reader.ReadUInt32(); // always 3?
                var unk2_b = Reader.ReadUInt32(); // always 2?
                var unk2_c = Reader.ReadUInt16(); // always 514?
                var unk2_d = Reader.ReadUInt16(); // always 514?
                var unk2_e = Reader.ReadUInt32();
                var unk2_f = Reader.ReadUInt32();
                var unk2_g = Reader.ReadUInt32();
                var unk2_h = Reader.ReadUInt32();
                var unk2_i = Reader.ReadUInt32();
                var unk2_j = Reader.ReadUInt32();
                var unk2_k = Reader.ReadUInt32();

                Reader.ReadBytes(176); // Chunk of mostly FF

                Reader.ReadBytes(256); // Chunk of 0s. padding?

                Console.WriteLine($"{unk2_a} {unk2_b} {unk2_c} {unk2_d} {unk2_e} {unk2_f} {unk2_g} {unk2_h} {unk2_i} {unk2_j} {unk2_k}");
            }

            // 3
            count = Reader.ReadUInt32();

            Console.WriteLine("[CHUNK 3] Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var previousPosition = Reader.BaseStream.Position;

                var name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = previousPosition + 128;

                var unk3_a = Reader.ReadInt32();
                var unk3_b = Reader.ReadInt32();
                var unk3_c = Reader.ReadInt32();
                var unk3_d = Reader.ReadInt32();
                var unk3_e = Reader.ReadInt32();
                var unk3_f = Reader.ReadInt32();

                Console.WriteLine($"{unk3_a} {unk3_b} {unk3_c} {unk3_d} {unk3_e} {unk3_f} {name}");
            }

            // 4 - Similar structure to chunk 2, same chunk size
            count = Reader.ReadUInt32();

            Console.WriteLine("[CHUNK 4] Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var unk4_a = Reader.ReadUInt32();
                var unk4_b = Reader.ReadUInt32();
                var unk4_c = Reader.ReadUInt16();
                var unk4_d = Reader.ReadUInt16();
                var unk4_e = Reader.ReadUInt32();
                var unk4_f = Reader.ReadUInt32();
                var unk4_g = Reader.ReadUInt32();
                var unk4_h = Reader.ReadUInt32();
                var unk4_i = Reader.ReadUInt32();

                Reader.ReadBytes(184); // Chunk of mostly FF

                Reader.ReadBytes(256); // Chunk of 0s. padding?

                Console.WriteLine($"{unk4_a} {unk4_b} {unk4_c} {unk4_d} {unk4_e} {unk4_f} {unk4_g} {unk4_h} {unk4_i}");
            }

            // 5 - Globals?
            count = Reader.ReadUInt32();

            Console.WriteLine("[CHUNK 5] Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var previousPosition = Reader.BaseStream.Position;

                var name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = previousPosition + 128; // ??

                var hasDesc = Reader.ReadInt32();
                var unk5_a = Reader.ReadInt32();

                var desc = string.Empty;

                if (hasDesc > 0)
                {
                    desc = Reader.ReadNullTermString(Encoding.UTF8);
                }

                Reader.BaseStream.Position = previousPosition + 200;

                var type = Reader.ReadInt32();
                var length = Reader.ReadInt32();

                Reader.BaseStream.Position = previousPosition + 480;

                // Don't know what content of this chunk is yet, but size seems to depend on type.
                // If we read the amount of bytes below per type the rest of the file will process as usual (and get to the LZMA stuff).
                // CHUNK SIZES:
                //  Type 0: 480
                //  Type 1: 480 + LENGTH + 4!
                //  Type 2: 480 (brushsplat_pc_40_ps.vcs)
                //  Type 5: 480 + LENGTH + 4! (debugoverlay_wireframe_pc_40_vs.vcs)
                //  Type 6: 480 + LENGTH + 4! (depth_only_pc_30_ps.vcs)
                //  Type 7: 480 + LENGTH + 4! (grasstile_preview_pc_41_ps.vcs)
                //  Type 10: 480 (brushsplat_pc_40_ps.vcs)
                //  Type 11: 480 (post_process_pc_30_ps.vcs)
                //  Type 13: 480 (spriteentity_pc_41_vs.vcs)
                // Needs further investigation. This is where parsing a lot of shaders break right now.
                if (length > -1 && type != 0 && type != 2 && type != 10 && type != 11 && type != 13)
                {
                    if (type != 1 && type != 5 && type != 6 && type != 7)
                    {
                        Console.WriteLine("!!! Unknown type of type " + type + " encountered at position " + (Reader.BaseStream.Position - 8) + ". Assuming normal sized chunk.");
                    }
                    else
                    {
                        var unk5_b = Reader.ReadBytes(length);
                        var unk5_c = Reader.ReadUInt32();
                    }
                }

                var unk5_d = Reader.ReadUInt32();
                Console.WriteLine($"{type} {length} {name} {hasDesc} {desc}");
            }

            // 6
            count = Reader.ReadUInt32();

            Console.WriteLine("[CHUNK 6] Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

            for (var i = 0; i < count; i++)
            {
                var unk6_a = Reader.ReadBytes(4); // unsure, maybe shorts or bytes
                var unk6_b = Reader.ReadUInt32(); // 12, 13, 14 or 15 in brushplat_pc_40_ps.vcs
                var unk6_c = Reader.ReadBytes(12); // FF
                var unk6_d = Reader.ReadUInt32();

                var previousPosition = Reader.BaseStream.Position;

                var name = Reader.ReadNullTermString(Encoding.UTF8);

                Reader.BaseStream.Position = previousPosition + 256;

                Console.WriteLine($"{unk6_b} {unk6_d} {name}");
            }

            // 7 - Input buffer layout
            count = Reader.ReadUInt32();

            Console.WriteLine("[CHUNK 7] Count: {0} - Offset: {1}", count, Reader.BaseStream.Position);

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

                    var bufferOffset = Reader.ReadUInt32(); // Offset in the buffer
                    var components = Reader.ReadUInt32(); // Number of components in this element
                    var componentSize = Reader.ReadUInt32(); // Number of floats per component
                    var repetitions = Reader.ReadUInt32(); // Number of repetitions?

                    Console.WriteLine("     Name: {0} - offset: {1} - components: {2} - compSize: {3} - num: {4}", subname, bufferOffset, components, componentSize, repetitions);
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
