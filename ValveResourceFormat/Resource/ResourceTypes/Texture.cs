using System;
using System.CodeDom.Compiler;
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

            reader.BaseStream.Position += extraDataOffset - 8; // 8 is 2 uint32s we just read

            while (extraDataCount-- > 0)
            {
                var type = reader.ReadUInt32();
                var offset = reader.ReadUInt32();
                var size = reader.ReadUInt32();

                reader.BaseStream.Position += offset + size - 8;
            }

            DataOffset = reader.BaseStream.Position;
        }

        public bool GenerateImage(Stream stream)
        {
            Reader.BaseStream.Position = DataOffset;

            switch (Format)
            {
                case VTexFormat.RGBA8888:
                    return GenerateRGBA8888(stream);

                case VTexFormat.DXT1:
                case VTexFormat.DXT5:
                    return GenerateDXT(stream);
            }

            throw new NotImplementedException(string.Format("Unhandled image type: {0}", Format));
        }

        private bool GenerateDXT(Stream stream)
        {
            if (NumMipLevels == 0)
            {
                throw new InvalidDataException("Invalid mip levels (must be at least 1).");
            }

            uint actualHeight = Height;

            if (NumMipLevels > 1)
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
                NumMipLevels,
                (byte)((Height & 0x00FF) >> 8),
                (byte)((Height & 0xFF00) >> 8),
                0
            };


            var a = new int[8];
            var c = new int[4];

            using (var writer = new BinaryWriter(stream))
            {
                for (ushort i = 0; i < Depth && i < 0xFF; i++)
                {
                    writer.Write(header);

                    for (var j = NumMipLevels; j > 0; j--)
                    {
                        for (var k = 0; k < Height / Math.Pow(2.0f, j - 1); ++k)
                        {
                            var nBlockOffset = 0;

                            var test = new byte[Width * 16];

                            for (var l = 0; l < Width / Math.Pow(2.0, j + 1); ++l)
                            {
                                
                                if (Format == VTexFormat.DXT1)
                                {
                                    Reader.ReadBytes(16).CopyTo(test, 0);
                                }
                                else if (Format == VTexFormat.DXT5)
                                {
                                    Reader.BaseStream.Position += 8;
                                    Reader.ReadBytes(8).CopyTo(test, 0);
                                }
                                else
                                {
                                    throw new NotImplementedException();
                                }

                                a[0] = test[0];
                                a[1] = test[1];
                                if (a[0] > a[1])
                                {
                                    a[2] = (6 * a[0] + 1 * a[1]) / 7;
                                    a[3] = (5 * a[0] + 2 * a[1]) / 7;
                                    a[4] = (4 * a[0] + 3 * a[1]) / 7;
                                    a[5] = (3 * a[0] + 4 * a[1]) / 7;
                                    a[6] = (2 * a[0] + 5 * a[1]) / 7;
                                    a[7] = (1 * a[0] + 6 * a[1]) / 7;
                                }
                                else
                                {
                                    a[2] = (4 * a[0] + 1 * a[1]) / 5;
                                    a[3] = (3 * a[0] + 2 * a[1]) / 5;
                                    a[4] = (2 * a[0] + 3 * a[1]) / 5;
                                    a[5] = (1 * a[0] + 4 * a[1]) / 5;
                                    a[6] = 0x00;
                                    a[7] = 0xFF;
                                }

                                c[0] = (((test[9] >> 3) & 0x1F) << 19) | ((((test[9] & 0x07) << 3) | ((test[8] >> 5) & 0x07)) << 10) | ((test[8] & 0x1F) << 3);
                                c[1] = (((test[11] >> 3) & 0x1F) << 19) | ((((test[11] & 0x07) << 3) | ((test[10] >> 5) & 0x07)) << 10) | ((test[10] & 0x1F) << 3);
                                if (c[0] > c[1])
                                {
                                    c[2] = ((2 * (c[0] & 0x000000FF) + 1 * (c[1] & 0x000000FF)) / 3) |
                                    (((2 * ((c[0] & 0x0000FF00) >> 8) + 1 * ((c[1] & 0x0000FF00) >> 8)) / 3) << 8) |
                                    (((2 * ((c[0] & 0x00FF0000) >> 16) + 1 * ((c[1] & 0x00FF0000) >> 16)) / 3) << 16);

                                    c[3] = ((1 * (c[0] & 0x000000FF) + 2 * (c[1] & 0x000000FF)) / 3) |
                                    (((1 * ((c[0] & 0x0000FF00) >> 8) + 2 * ((c[1] & 0x0000FF00) >> 8)) / 3) << 8) |
                                    (((1 * ((c[0] & 0x00FF0000) >> 16) + 2 * ((c[1] & 0x00FF0000) >> 16)) / 3) << 16);
                                }
                                else
                                {
                                    c[2] = (((c[0] & 0x000000FF) + (c[1] & 0x000000FF)) / 2) |
                                    (((((c[0] & 0x0000FF00) >> 8) + ((c[1] & 0x0000FF00) >> 8)) / 2) << 8) |
                                    (((((c[0] & 0x00FF0000) >> 16) + ((c[1] & 0x00FF0000) >> 16)) / 2) << 16);

                                    c[3] = 0x00000000;
                                }

                                for (var m = 0; m < 2; ++m)
                                {
                                    var ai = test[2 + (m * 3)];
                                    var ci = test[12 + (m * 2)];

                                    for (var n = 0; n < 4; ++n)
                                    {
                                        test[nBlockOffset++] = (byte)c[(ci >> (n * 2)) & 0x03];
                                        test[nBlockOffset++] = (byte)c[(ci >> (n * 2)) & 0x03];
                                        test[nBlockOffset++] = (byte)c[(ci >> (n * 2)) & 0x03];
                                        if (Format == VTexFormat.DXT5)
                                            test[nBlockOffset++] = (byte)a[(ai >> (n * 3)) & 0x07];
                                        else if (Format == VTexFormat.DXT1)
                                            test[nBlockOffset++] = (byte)0xFF;
                                    }
                                    nBlockOffset += (Width * 4) - 16;
                                    for (var n = 0; n < 4; ++n)
                                    {
                                        test[nBlockOffset++] = (byte)c[(ci >> (n * 2)) & 0x03];
                                        test[nBlockOffset++] = (byte)c[(ci >> (n * 2)) & 0x03];
                                        test[nBlockOffset++] = (byte)c[(ci >> (n * 2)) & 0x03];
                                        if (Format == VTexFormat.DXT5)
                                            test[nBlockOffset++] = (byte)a[(ai >> (n * 3)) & 0x07];
                                        else if (Format == VTexFormat.DXT1)
                                            test[nBlockOffset++] = (byte)0xFF;
                                    }
                                    nBlockOffset += (Width * 4) - 16;
                                }
                                nBlockOffset = (l + 1) * 16;
                            }

                            writer.Write(test);
                        }

                        writer.Seek(Width * 4, SeekOrigin.Current);
                    }
                }
            }

            return true;
        }

        private bool GenerateRGBA8888(Stream stream)
        {
            if (NumMipLevels == 0)
            {
                throw new InvalidDataException("Invalid mip levels (must be at least 1).");
            }

            uint actualHeight = Height;

            if (NumMipLevels > 1)
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
                NumMipLevels,
                (byte)((Height & 0x00FF) >> 8),
                (byte)((Height & 0xFF00) >> 8),
                0
            };
            
            using (var writer = new BinaryWriter(stream))
            {
                for (ushort i = 0; i < Depth && i < 0xFF; i++)
                {
                    writer.Write(header);

                    for (var j = NumMipLevels; j > 0; j--)
                    {
                        for (var k = 0; k < Height / Math.Pow(2.0f, j - 1); ++k)
                        {
                            var test = Reader.ReadBytes((int)((4 * Width) / Math.Pow(2.0f, j - 1)));

                            for (var l = 0; l < Width * 4; l += 4)
                            {
                                var c = test[l];
                                test[l] = test[l + 2];
                                test[l + 2] = c;
                            }

                            writer.Write(test);
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
                
                writer.WriteLine("{0,-12} = {1} entries:", "Extra Data", 0);

                return output.ToString();
            }
        }
    }
}
