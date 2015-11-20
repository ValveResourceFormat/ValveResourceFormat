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

        public Dictionary<VTexExtraData, byte[]> ExtraData { get; private set; }

        public Texture()
        {
            ExtraData = new Dictionary<VTexExtraData, byte[]>();
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

            if (extraDataCount > 0)
            {
                reader.BaseStream.Position += extraDataOffset - 8; // 8 is 2 uint32s we just read

                for (var i = 0; i < extraDataCount; i++)
                {
                    var type = reader.ReadUInt32();
                    var offset = reader.ReadUInt32() - 8;
                    var size = reader.ReadUInt32();

                    var prevOffset = reader.BaseStream.Position;

                    reader.BaseStream.Position += offset;

                    ExtraData.Add((VTexExtraData)type, reader.ReadBytes((int)size));

                    reader.BaseStream.Position = prevOffset;
                }
            }

            DataOffset = this.Offset + this.Size;
        }

        public Bitmap GenerateBitmap()
        {
            Reader.BaseStream.Position = DataOffset;

            switch (Format)
            {
                case VTexFormat.RGBA8888:
                    for (ushort i = 0; i < Depth && i < 0xFF; ++i)
                    {
                        // Horribly skip all mipmaps
                        // TODO: Either this needs to be optimized, or allow saving each individual mipmap
                        for (byte j = NumMipLevels; j > 0; j--)
                        {
                            if (j == 1) break;

                            for (var k = 0; k < Height / Math.Pow(2.0, j - 1); ++k)
                            {
                                Reader.BaseStream.Position += (int)((4 * Width) / Math.Pow(2.0f, j - 1));
                            }
                        }

                        return ReadRGBA8888(Reader, Width, Height);
                    }

                    break;

                case VTexFormat.RGBA16161616F:
                    return ReadRGBA16161616F(Reader, Width, Height);

                case VTexFormat.DXT1:
                    for (ushort i = 0; i < Depth && i < 0xFF; ++i)
                    {
                        // Horribly skip all mipmaps
                        // TODO: Either this needs to be optimized, or allow saving each individual mipmap
                        for (byte j = NumMipLevels; j > 0; j--)
                        {
                            if (j == 1) break;

                            for (var k = 0; k < Height / Math.Pow(2.0, j + 1); ++k)
                            {
                                for (var l = 0; l < Width / Math.Pow(2.0, j + 1); ++l)
                                {
                                    Reader.BaseStream.Position += 8;
                                }
                            }
                        }

                        return ThirdParty.DDSImage.UncompressDXT1(Reader, Width, Height);
                    }

                    break;

                case VTexFormat.DXT5:
                    for (ushort i = 0; i < Depth && i < 0xFF; ++i)
                    {
                        // Horribly skip all mipmaps
                        // TODO: Either this needs to be optimized, or allow saving each individual mipmap
                        for (byte j = NumMipLevels; j > 0; j--)
                        {
                            if (j == 1) break;

                            for (var k = 0; k < Height / Math.Pow(2.0, j + 1); ++k)
                            {
                                for (var l = 0; l < Width / Math.Pow(2.0, j + 1); ++l)
                                {
                                    Reader.BaseStream.Position += 16;
                                }
                            }
                        }

                        return ThirdParty.DDSImage.UncompressDXT5(Reader, Width, Height);
                    }

                    break;

                case (VTexFormat)17:
                case (VTexFormat)18:
                case VTexFormat.PNG:
                    return ReadPNG();
            }

            throw new NotImplementedException(string.Format("Unhandled image type: {0}", Format));
        }

        private Bitmap ReadPNG()
        {
            // TODO: Seems stupid to provide stream length
            using (var ms = new MemoryStream(Reader.ReadBytes((int)Reader.BaseStream.Length)))
            {
                return new Bitmap(Image.FromStream(ms));
            }
        }

        private static Bitmap ReadRGBA8888(BinaryReader r, int w, int h)
        {
            var res = new Bitmap(w, h);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var rawColor = r.ReadInt32();

                    var color = Color.FromArgb(
                        (rawColor >> 24) & 0x0FF,
                        rawColor & 0x0FF,
                        (rawColor >> 8) & 0x0FF,
                        (rawColor >> 16) & 0x0FF
                    );

                    res.SetPixel(x, y, color);
                }
            }

            return res;
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

                int entry = 0;

                foreach (var b in ExtraData)
                {
                    writer.WriteLine("{0,-12}   [ Entry {1}: VTEX_EXTRA_DATA_{2} - {3} bytes ]", "", entry++, b.Key, b.Value.Length);
                }

                return output.ToString();
            }
        }
    }
}
