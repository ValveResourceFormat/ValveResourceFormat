using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.ResourceTypes
{
    public class Texture : ResourceData
    {
        private BinaryReader Reader;
        private long DataOffset;
        private Resource Resource;

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

        public ushort NonPow2Width { get; private set; }

        public ushort NonPow2Height { get; private set; }

        public Texture()
        {
            ExtraData = new Dictionary<VTexExtraData, byte[]>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            Reader = reader;
            Resource = resource;

            reader.BaseStream.Position = Offset;

            Version = reader.ReadUInt16();

            if (Version != 1)
            {
                throw new InvalidDataException(string.Format("Unknown vtex version. ({0} != expected 1)", Version));
            }

            Flags = (VTexFlags)reader.ReadUInt16();

            Reflectivity = new[]
            {
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
            };
            Width = reader.ReadUInt16();
            Height = reader.ReadUInt16();
            Depth = reader.ReadUInt16();
            NonPow2Width = 0;
            NonPow2Height = 0;
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

                    if ((VTexExtraData)type == VTexExtraData.FILL_TO_POWER_OF_TWO)
                    {
                        reader.ReadUInt16();
                        var nw = reader.ReadUInt16();
                        var nh = reader.ReadUInt16();
                        if (nw > 0 && nh > 0 && Width >= nw && Height >= nh)
                        {
                            NonPow2Width = nw;
                            NonPow2Height = nh;
                        }

                        reader.BaseStream.Position -= 6;
                    }

                    ExtraData.Add((VTexExtraData)type, reader.ReadBytes((int)size));

                    reader.BaseStream.Position = prevOffset;
                }
            }

            DataOffset = Offset + Size;
        }

        public SKBitmap GenerateBitmap()
        {
            Reader.BaseStream.Position = DataOffset;

            switch (Format)
            {
                case VTexFormat.DXT1:
                    SkipMipmaps(8);

                    return DDSImage.UncompressDXT1(Reader, Width, Height, NonPow2Width, NonPow2Height);

                case VTexFormat.DXT5:
                    var yCoCg = false;

                    if (Resource.EditInfo.Structs.ContainsKey(ResourceEditInfo.REDIStruct.SpecialDependencies))
                    {
                        var specialDeps = (SpecialDependencies)Resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.SpecialDependencies];

                        yCoCg = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Image YCoCg Conversion");
                    }

                    SkipMipmaps(16);

                    return DDSImage.UncompressDXT5(Reader, Width, Height, yCoCg, NonPow2Width, NonPow2Height);

                case VTexFormat.I8:
                    break;

                case VTexFormat.RGBA8888:
                    SkipMipmaps(4);

                    return ReadRGBA8888(Reader, Width, Height);

                case VTexFormat.R16:
                    break;

                case VTexFormat.RG1616:
                    break;

                case VTexFormat.RGBA16161616:
                    break;

                case VTexFormat.R16F:
                    break;

                case VTexFormat.RG1616F:
                    break;

                case VTexFormat.RGBA16161616F:
                    SkipMipmaps(8);

                    return ReadRGBA16161616F(Reader, Width, Height);

                case VTexFormat.R32F:
                    break;

                case VTexFormat.RG3232F:
                    break;

                case VTexFormat.RGB323232F:
                    break;

                case VTexFormat.RGBA32323232F:
                    break;

                case VTexFormat.JPG:
                case VTexFormat.PNG2:
                case VTexFormat.PNG:
                    return ReadBuffer();
            }

            throw new NotImplementedException(string.Format("Unhandled image type: {0}", Format));
        }

        private void SkipMipmaps(int bytesPerPixel)
        {
            for (var j = NumMipLevels; j > 1; j--)
            {
                var size = Math.Pow(2.0f, j + 1);

                Reader.BaseStream.Position += (int)((bytesPerPixel * Width) / size * (Height / size));
            }
        }

        private SKBitmap ReadBuffer()
        {
            return SKBitmap.Decode(Reader.ReadBytes((int)Reader.BaseStream.Length));
        }

        private static SKBitmap ReadRGBA8888(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    res.SetPixel(x, y, new SKColor(r.ReadUInt32()));
                }
            }

            return res;
        }

        private static SKBitmap ReadRGBA16161616F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var red = (byte)(HalfTypeHelper.Convert(r.ReadUInt16()) * 255);
                    var green = (byte)(HalfTypeHelper.Convert(r.ReadUInt16()) * 255);
                    var blue = (byte)(HalfTypeHelper.Convert(r.ReadUInt16()) * 255);
                    var alpha = (byte)(HalfTypeHelper.Convert(r.ReadUInt16()) * 255);

                    res.SetPixel(x, y, new SKColor(red, green, blue, alpha));
                }
            }

            return res;
        }

        public override string ToString()
        {
            using (var writer = new IndentedTextWriter())
            {
                writer.WriteLine("{0,-12} = {1}", "VTEX Version", Version);
                writer.WriteLine("{0,-12} = {1}", "Width", Width);
                writer.WriteLine("{0,-12} = {1}", "Height", Height);
                writer.WriteLine("{0,-12} = {1}", "Depth", Depth);
                writer.WriteLine("{0,-12} = {1}", "NonPow2W", NonPow2Width);
                writer.WriteLine("{0,-12} = {1}", "NonPow2H", NonPow2Height);
                writer.WriteLine("{0,-12} = ( {1:F6}, {2:F6}, {3:F6}, {4:F6} )", "Reflectivity", Reflectivity[0], Reflectivity[1], Reflectivity[2], Reflectivity[3]);
                writer.WriteLine("{0,-12} = {1}", "NumMipLevels", NumMipLevels);
                writer.WriteLine("{0,-12} = {1}", "Picmip0Res", Picmip0Res);
                writer.WriteLine("{0,-12} = {1} (VTEX_FORMAT_{2})", "Format", (int)Format, Format);
                writer.WriteLine("{0,-12} = 0x{1:X8}", "Flags", (int)Flags);

                foreach (Enum value in Enum.GetValues(Flags.GetType()))
                {
                    if (Flags.HasFlag(value))
                    {
                        writer.WriteLine("{0,-12} | 0x{1:X8} = VTEX_FLAG_{2}", string.Empty, Convert.ToInt32(value), value);
                    }
                }

                writer.WriteLine("{0,-12} = {1} entries:", "Extra Data", ExtraData.Count);

                var entry = 0;

                foreach (var b in ExtraData)
                {
                    writer.WriteLine("{0,-12}   [ Entry {1}: VTEX_EXTRA_DATA_{2} - {3} bytes ]", string.Empty, entry++, b.Key, b.Value.Length);
                }

                return writer.ToString();
            }
        }
    }
}
