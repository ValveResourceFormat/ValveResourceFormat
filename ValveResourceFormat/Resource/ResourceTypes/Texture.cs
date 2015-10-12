using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace ValveResourceFormat.ResourceTypes
{
    public class Texture : Blocks.ResourceData
    {
        private BinaryReader Reader;
        private long DataOffset;

        public ushort Version { get; private set; }

        public ushort Width { get; private set; }

        public ushort Height { get; private set; }

        public ushort Depth { get; private set; }

        public float[] Reflectivity { get; private set; }

        public VTexFlags Flags { get; private set; }

        public VTexFormat Format { get; private set; }

        public byte NumMipLevels { get; private set; }

        public uint Picmip0Res { get; private set; }

        public List<byte[]> ExtraData { get; private set; }

        public Texture()
        {
            ExtraData = new List<byte[]>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            Reader = reader;

            reader.BaseStream.Position = this.Offset;

            Version = reader.ReadUInt16();

            if (Version != 1)
            {
                throw new InvalidDataException(string.Format("Unknown vtex version. ({0} != expected 1)", Version));
            }

            Flags = (VTexFlags)reader.ReadUInt16();

            Reflectivity = new []
            {
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            };
            
            Width = reader.ReadUInt16();
            Height = reader.ReadUInt16();
            Depth = reader.ReadUInt16();
            Format = (VTexFormat)reader.ReadByte();
            NumMipLevels = reader.ReadByte();
            Picmip0Res = reader.ReadUInt32();

            var extraDataOffset = reader.ReadUInt32();
            var extraDataCount = reader.ReadUInt32();

            uint spacing = 0;

            if (extraDataCount > 0)
            {
                reader.BaseStream.Position += extraDataOffset - 8; // 8 is 2 uint32s we just read

                while (extraDataCount-- > 0)
                {
                    var type = reader.ReadUInt32();
                    var offset = reader.ReadUInt32() - 8;
                    var size = reader.ReadUInt32();

                    Console.WriteLine(type + " " + offset + " " + size);

                    // type 1 = fallback data it seems

                    var prevOffset = reader.BaseStream.Position;

                    reader.BaseStream.Position += offset;

                    ExtraData.Add(reader.ReadBytes((int)size));

                    reader.BaseStream.Position = prevOffset;

                    spacing += offset + size;
                }
            }

            DataOffset = reader.BaseStream.Position + spacing;
        }

        public Bitmap GenerateBitmap()
        {
            Reader.BaseStream.Position = DataOffset;

            switch (Format)
            {
                case VTexFormat.RGBA8888:
                    //return GenerateRGBA8888(stream);
                    return ThirdParty.DDSImage.ReadLinearImage(Reader, Width, Height);

                case VTexFormat.RGBA16161616F:
                    return ReadRGBA16161616F(Reader, Width, Height);

                case VTexFormat.DXT1:
                    return ThirdParty.DDSImage.UncompressDXT1(Reader, Width, Height);

                case VTexFormat.DXT5:
                    return ThirdParty.DDSImage.UncompressDXT5(Reader, Width, Height);
            }

            throw new NotImplementedException(string.Format("Unhandled image type: {0}", Format));
        }

        private static Bitmap ReadRGBA16161616F(BinaryReader r, int w, int h)
        {
            var res = new Bitmap(w, h);

            while (h-- > 0)
            {
                while (w-- > 0)
                {
                    var red = (int)r.ReadDouble();
                    var green = (int)r.ReadDouble();
                    var blue = (int)r.ReadDouble();
                    var alpha = (int)r.ReadDouble();

                    res.SetPixel(w, h, Color.FromArgb(alpha, red, green, blue));
                }
            }

            return res;
        }

        private bool GenerateRGBA8888(Stream stream, bool generateMipmaps = false)
        {
            if (NumMipLevels == 0)
            {
                throw new InvalidDataException("Invalid mip levels (must be at least 1).");
            }

            uint actualHeight = Height;

            if (NumMipLevels > 1 && generateMipmaps)
            {
                actualHeight = (uint)(Height * (2.0 - Math.Pow(0.5, NumMipLevels - 1)));
                actualHeight += (uint)NumMipLevels - 1;
            }

            var header = new byte[]
            {
                0,
                0,
                2,
                0, 0, 0, 0,
                0,
                0, 0, 0, 0,
                (byte)(Width & 0x00FF),
                (byte)((Width & 0xFF00) >> 8),
                (byte)(actualHeight & 0x00FF),
                (byte)((actualHeight & 0xFF00) >> 8),
                0x20,
                0x20,
                generateMipmaps ? NumMipLevels : (byte)1,
                (byte)((Height & 0x00FF) >> 8),
                (byte)((Height & 0xFF00) >> 8),
                0
            };
            
            using (var writer = new BinaryWriter(stream))
            {
                //for (ushort i = 0; i < Depth && i < 0xFF; i++)
                {
                    writer.Write(header);

                    for (var j = NumMipLevels; j > 0; j--)
                    {
                        for (var k = 0; k < Height / Math.Pow(2.0f, j - 1); ++k)
                        {
                            var test = Reader.ReadBytes((int)((4 * Width) / Math.Pow(2.0f, j - 1)));

                            if (generateMipmaps && j != 1)
                            {
                                continue;
                            }

                            for (var l = 0; l < Width * 4; l += 4)
                            {
                                var c = test[l];
                                test[l] = test[l + 2];
                                test[l + 2] = c;
                            }

                            writer.Write(test);
                        }

                        if (!generateMipmaps)
                        {
                            break;
                        }

                        writer.Seek(Width * 4, SeekOrigin.Current);
                    }
                }
            }

            return true;
        }

        public override string ToString()
        {
            using (var output = new StringWriter())
            using (var writer = new IndentedTextWriter(output, "\t"))
            {
                writer.WriteLine("{0,-12} = {1}", "VTEX Version", Version);
                writer.WriteLine("{0,-12} = {1}", "Width", Width);
                writer.WriteLine("{0,-12} = {1}", "Height", Height);
                writer.WriteLine("{0,-12} = {1}", "Depth", Depth);
                writer.WriteLine("{0,-12} = ( {1:F6}, {2:F6}, {3:F6}, {4:F6} )", "Reflectivity", Reflectivity[0], Reflectivity[1], Reflectivity[2], Reflectivity[3]);
                writer.WriteLine("{0,-12} = {1}", "NumMipLevels", NumMipLevels);
                writer.WriteLine("{0,-12} = {1}", "Picmip0Res", Picmip0Res);
                writer.WriteLine("{0,-12} = {1} (VTEX_FORMAT_{2})", "Format", (int)Format, Format);
                writer.WriteLine("{0,-12} = 0x{1:X8}", "Flags", (int)Flags);

                foreach (Enum value in Enum.GetValues(Flags.GetType()))
                {
                    if (Flags.HasFlag(value))
                    {
                        writer.WriteLine("{0,-12} | 0x{1:X8} = VTEX_FLAG_{2}", "", Convert.ToInt32(value), value);
                    }
                }
                
                writer.WriteLine("{0,-12} = {1} entries:", "Extra Data", ExtraData.Count);

                writer.Indent++;
                foreach (var b in ExtraData)
                {
                    writer.WriteLine(System.Text.Encoding.ASCII.GetString(b));
                }
                writer.Indent--;

                return output.ToString();
            }
        }
    }
}
