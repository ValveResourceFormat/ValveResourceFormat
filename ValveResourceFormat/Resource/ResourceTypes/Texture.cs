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
            var imageInfo = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var data = new byte[imageInfo.RowBytes * h];

            var bytes = r.ReadBytes(w * h * 8);
            var log = 0d;

            for (int i = 0, j = 0; i < bytes.Length; i += 8, j += 4)
            {
                var hr = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 0));
                var hg = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 2));
                var hb = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 4));
                var lum = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                log += Math.Log(0.0000000001d + lum);
            }

            log = Math.Exp(log / (w * h));

            for (int i = 0, j = 0; i < bytes.Length; i += 8, j += 4)
            {
                var hr = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 0));
                var hg = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 2));
                var hb = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 4));
                var ha = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 6));

                var y = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                var u = (hb - y) * 0.565f;
                var v = (hr - y) * 0.713f;

                var mul = 4.0f * y / log;
                mul = mul / (1.0f + mul);
                mul /= y;

                hr = (float)Math.Pow((y + (1.403f * v)) * mul, 2.25f);
                hg = (float)Math.Pow((y - (0.344f * u) - (0.714f * v)) * mul, 2.25f);
                hb = (float)Math.Pow((y + (1.770f * u)) * mul, 2.25f);

#pragma warning disable SA1503
                if (hr < 0) hr = 0;
                if (hr > 1) hr = 1;
                if (hg < 0) hg = 0;
                if (hg > 1) hg = 1;
                if (hb < 0) hb = 0;
                if (hb > 1) hb = 1;
#pragma warning restore SA1503

                data[j + 0] = (byte)(hr * 255); // r
                data[j + 1] = (byte)(hg * 255); // g
                data[j + 2] = (byte)(hb * 255); // b
                data[j + 3] = (byte)(ha * 255); // a
            }

            return DDSImage.CreateBitmap(imageInfo, ref data);
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
