using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using K4os.Compression.LZ4;
using SkiaSharp;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class Texture : ResourceData
    {
        private const short MipmapLevelToExtract = 0; // for debugging purposes

        public class SpritesheetData
        {
            public class Sequence
            {
                public class Frame
                {
                    public class Image
                    {
                        public Vector2 StartMins { get; set; }
                        public Vector2 StartMaxs { get; set; }

                        public Vector2 EndMins { get; set; }
                        public Vector2 EndMaxs { get; set; }
                    }

                    public Image[] Images { get; set; }

                    public float DisplayTime { get; set; }
                }

                public Frame[] Frames { get; set; }
                public float FramesPerSecond { get; set; }
                public string Name { get; set; }
                public bool Clamp { get; set; }
                public bool AlphaCrop { get; set; }
                public bool NoColor { get; set; }
                public bool NoAlpha { get; set; }
                public Dictionary<string, float> FloatParams { get; } = new();
            }

            public Sequence[] Sequences { get; set; }
        }

        public int BlockSize => Format switch
        {
            VTexFormat.DXT1 => 8,
            VTexFormat.DXT5 => 16,
            VTexFormat.RGBA8888 => 4,
            VTexFormat.R16 => 2,
            VTexFormat.RG1616 => 4,
            VTexFormat.RGBA16161616 => 8,
            VTexFormat.R16F => 2,
            VTexFormat.RG1616F => 4,
            VTexFormat.RGBA16161616F => 8,
            VTexFormat.R32F => 4,
            VTexFormat.RG3232F => 8,
            VTexFormat.RGB323232F => 12,
            VTexFormat.RGBA32323232F => 16,
            VTexFormat.BC6H => 16,
            VTexFormat.BC7 => 16,
            VTexFormat.IA88 => 2,
            VTexFormat.ETC2 => 8,
            VTexFormat.ETC2_EAC => 16,
            VTexFormat.BGRA8888 => 4,
            VTexFormat.ATI1N => 8,
            _ => 1,
        };

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

        private int[] CompressedMips;
        private bool IsActuallyCompressedMips;

        private float[] RadianceCoefficients;

        public ushort ActualWidth => NonPow2Width > 0 ? NonPow2Width : Width;
        public ushort ActualHeight => NonPow2Height > 0 ? NonPow2Height : Height;

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
                throw new UnexpectedMagicException("Unknown vtex version", Version, nameof(Version));
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
                    var type = (VTexExtraData)reader.ReadUInt32();
                    var offset = reader.ReadUInt32() - 8;
                    var size = reader.ReadUInt32();

                    var prevOffset = reader.BaseStream.Position;

                    reader.BaseStream.Position += offset;
                    ExtraData.Add(type, reader.ReadBytes((int)size));
                    reader.BaseStream.Position -= size;

                    if (type == VTexExtraData.FILL_TO_POWER_OF_TWO)
                    {
                        reader.ReadUInt16();
                        var nw = reader.ReadUInt16();
                        var nh = reader.ReadUInt16();
                        if (nw > 0 && nh > 0 && Width >= nw && Height >= nh)
                        {
                            NonPow2Width = nw;
                            NonPow2Height = nh;
                        }
                    }
                    else if (type == VTexExtraData.COMPRESSED_MIP_SIZE)
                    {
                        var int1 = reader.ReadUInt32(); // 1?
                        var mipsOffset = reader.ReadUInt32();
                        var mips = reader.ReadUInt32();

                        if (int1 != 1 && int1 != 0)
                        {
                            throw new InvalidDataException($"int1 got: {int1}");
                        }

                        IsActuallyCompressedMips = int1 == 1; // TODO: Verify whether this int is the one that actually controls compression

                        CompressedMips = new int[mips];

                        reader.BaseStream.Position += mipsOffset - 8;

                        for (var mip = 0; mip < mips; mip++)
                        {
                            CompressedMips[mip] = reader.ReadInt32();
                        }
                    }
                    else if (type == VTexExtraData.CUBEMAP_RADIANCE_SH)
                    {
                        var coeffsOffset = reader.ReadUInt32();
                        var coeffs = reader.ReadUInt32();

                        //Spherical Harmonics
                        RadianceCoefficients = new float[coeffs];

                        reader.BaseStream.Position += coeffsOffset - 8;

                        for (var c = 0; c < coeffs; c++)
                        {
                            RadianceCoefficients[c] = reader.ReadSingle();
                        }
                    }

                    reader.BaseStream.Position = prevOffset;
                }
            }

            DataOffset = Offset + Size;
        }

        public SpritesheetData GetSpriteSheetData()
        {
            if (ExtraData.TryGetValue(VTexExtraData.SHEET, out var bytes))
            {
                using var memoryStream = new MemoryStream(bytes);
                using var reader = new BinaryReader(memoryStream);
                var version = reader.ReadUInt32();

                if (version != 8)
                {
                    throw new UnexpectedMagicException("Unknown version", version, nameof(version));
                }

                var numSequences = reader.ReadUInt32();

                var sequences = new SpritesheetData.Sequence[numSequences];

                for (var s = 0; s < numSequences; s++)
                {
                    var sequence = new SpritesheetData.Sequence();
                    var id = reader.ReadUInt32();
                    sequence.Clamp = reader.ReadBoolean();
                    sequence.AlphaCrop = reader.ReadBoolean();
                    sequence.NoColor = reader.ReadBoolean();
                    sequence.NoAlpha = reader.ReadBoolean();
                    var framesOffset = reader.BaseStream.Position + reader.ReadUInt32();
                    var numFrames = reader.ReadUInt32();
                    sequence.FramesPerSecond = reader.ReadSingle();
                    var nameOffset = reader.BaseStream.Position + reader.ReadUInt32();
                    var floatParamsOffset = reader.BaseStream.Position + reader.ReadUInt32();
                    var floatParamsCount = reader.ReadUInt32();

                    var endOfHeaderOffset = reader.BaseStream.Position;

                    // Seek to start of the sequence data
                    reader.BaseStream.Position = nameOffset;

                    sequence.Name = reader.ReadNullTermString(Encoding.UTF8);
                    // There may be alignment bytes after the name, so the data always falls on 4-byte boundary

                    if (floatParamsCount > 0)
                    {
                        reader.BaseStream.Position = floatParamsOffset;

                        for (var p = 0; p < floatParamsCount; p++)
                        {
                            var floatParamNameOffset = reader.BaseStream.Position + reader.ReadUInt32();
                            var floatValue = reader.ReadSingle();

                            var offsetNextParam = reader.BaseStream.Position;
                            reader.BaseStream.Position = floatParamNameOffset;
                            var floatName = reader.ReadNullTermString(Encoding.UTF8);
                            reader.BaseStream.Position = offsetNextParam;

                            sequence.FloatParams.Add(floatName, floatValue);
                        }
                    }

                    reader.BaseStream.Position = framesOffset;

                    sequence.Frames = new SpritesheetData.Sequence.Frame[numFrames];

                    for (var f = 0; f < numFrames; f++)
                    {
                        var displayTime = reader.ReadSingle();
                        var imageOffset = reader.BaseStream.Position + reader.ReadUInt32();
                        var imageCount = reader.ReadUInt32();
                        var originalOffset = reader.BaseStream.Position;

                        var images = new SpritesheetData.Sequence.Frame.Image[imageCount];
                        sequence.Frames[f] = new SpritesheetData.Sequence.Frame
                        {
                            DisplayTime = displayTime,
                            Images = images,
                        };

                        reader.BaseStream.Position = imageOffset;

                        for (var i = 0; i < images.Length; i++)
                        {
                            images[i] = new SpritesheetData.Sequence.Frame.Image
                            {
                                // uvCropped
                                StartMins = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                                StartMaxs = new Vector2(reader.ReadSingle(), reader.ReadSingle()),

                                // uvUncropped
                                EndMins = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                                EndMaxs = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                            };
                        }

                        reader.BaseStream.Position = originalOffset;
                    }

                    reader.BaseStream.Position = endOfHeaderOffset;

                    sequences[s] = sequence;
                }

                return new SpritesheetData
                {
                    Sequences = sequences,
                };
            }

            return null;
        }

        public SKBitmap GenerateBitmap()
        {
            Reader.BaseStream.Position = DataOffset;

            var width = MipLevelSize(ActualWidth, MipmapLevelToExtract);
            var height = MipLevelSize(ActualHeight, MipmapLevelToExtract);
            var blockWidth = MipLevelSize(Width, MipmapLevelToExtract);
            var blockHeight = MipLevelSize(Height, MipmapLevelToExtract);

            var skiaBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            SkipMipmaps();

            switch (Format)
            {
                case VTexFormat.DXT1:
                    return TextureDecompressors.UncompressDXT1(skiaBitmap, GetTextureSpan(), blockWidth, blockHeight);

                case VTexFormat.DXT5:
                    var yCoCg = false;
                    var normalize = false;
                    var invert = false;
                    var hemiOct = false;

                    if (Resource.EditInfo.Structs.ContainsKey(ResourceEditInfo.REDIStruct.SpecialDependencies))
                    {
                        var specialDeps = (SpecialDependencies)Resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.SpecialDependencies];

                        yCoCg = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Image YCoCg Conversion");
                        normalize = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Image NormalizeNormals");
                        invert = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version LegacySource1InvertNormals");
                        hemiOct = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Mip HemiOctAnisoRoughness");
                    }

                    return TextureDecompressors.UncompressDXT5(skiaBitmap, GetTextureSpan(), blockWidth, blockHeight, yCoCg, normalize, invert, hemiOct);

                case VTexFormat.I8:
                    return TextureDecompressors.ReadI8(skiaBitmap, GetTextureSpan());

                case VTexFormat.RGBA8888:
                    return TextureDecompressors.ReadRGBA8888(skiaBitmap, GetTextureSpan());

                case VTexFormat.R16:
                    return TextureDecompressors.ReadR16(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.RG1616:
                    return TextureDecompressors.ReadRG1616(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.RGBA16161616:
                    return TextureDecompressors.ReadRGBA16161616(skiaBitmap, GetTextureSpan());

                case VTexFormat.R16F:
                    return TextureDecompressors.ReadR16F(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.RG1616F:
                    return TextureDecompressors.ReadRG1616F(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.RGBA16161616F:
                    return TextureDecompressors.ReadRGBA16161616F(skiaBitmap, GetTextureSpan());

                case VTexFormat.R32F:
                    return TextureDecompressors.ReadR32F(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.RG3232F:
                    return TextureDecompressors.ReadRG3232F(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.RGB323232F:
                    return TextureDecompressors.ReadRGB323232F(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.RGBA32323232F:
                    return TextureDecompressors.ReadRGBA32323232F(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.BC6H:
                    return BPTC.BPTCDecoders.UncompressBC6H(GetDecompressedBuffer(), Width, Height);

                case VTexFormat.BC7:
                    bool hemiOctRB = false;
                    invert = false;
                    if (Resource.EditInfo.Structs.ContainsKey(ResourceEditInfo.REDIStruct.SpecialDependencies))
                    {
                        var specialDeps = (SpecialDependencies)Resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.SpecialDependencies];
                        hemiOctRB = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Mip HemiOctIsoRoughness_RG_B");
                        invert = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version LegacySource1InvertNormals");
                    }

                    return BPTC.BPTCDecoders.UncompressBC7(GetDecompressedBuffer(), Width, Height, hemiOctRB, invert);

                case VTexFormat.ATI2N:
                    normalize = false;
                    if (Resource.EditInfo.Structs.ContainsKey(ResourceEditInfo.REDIStruct.SpecialDependencies))
                    {
                        var specialDeps = (SpecialDependencies)Resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.SpecialDependencies];
                        normalize = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Image NormalizeNormals");
                    }

                    return TextureDecompressors.UncompressATI2N(skiaBitmap, GetTextureSpan(), Width, Height, normalize);

                case VTexFormat.IA88:
                    return TextureDecompressors.ReadIA88(skiaBitmap, GetTextureSpan());

                case VTexFormat.ATI1N:
                    return TextureDecompressors.UncompressATI1N(skiaBitmap, GetTextureSpan(), Width, Height);

                // TODO: Are we sure DXT5 and RGBA8888 are just raw buffers?
                case VTexFormat.JPEG_DXT5:
                case VTexFormat.JPEG_RGBA8888:
                    return SKBitmap.Decode(Reader.ReadBytes((int)(Reader.BaseStream.Length - Reader.BaseStream.Position)));

                case VTexFormat.PNG_DXT5:
                case VTexFormat.PNG_RGBA8888:
                    return SKBitmap.Decode(Reader.ReadBytes(CalculatePngSize()));

                case VTexFormat.ETC2:
                    // TODO: Rewrite EtcDecoder to work on skia span directly
                    var etc = new Etc.EtcDecoder();
                    var data = new byte[skiaBitmap.RowBytes * skiaBitmap.Height];
                    etc.DecompressETC2(GetDecompressedTextureAtMipLevel(0), width, height, data);
                    var gcHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
                    skiaBitmap.InstallPixels(skiaBitmap.Info, gcHandle.AddrOfPinnedObject(), skiaBitmap.RowBytes, (address, context) => { gcHandle.Free(); }, null);
                    break;

                case VTexFormat.ETC2_EAC:
                    // TODO: Rewrite EtcDecoder to work on skia span directly
                    var etc2 = new Etc.EtcDecoder();
                    var data2 = new byte[skiaBitmap.RowBytes * skiaBitmap.Height];
                    etc2.DecompressETC2A8(GetDecompressedTextureAtMipLevel(0), width, height, data2);
                    var gcHandle2 = GCHandle.Alloc(data2, GCHandleType.Pinned);
                    skiaBitmap.InstallPixels(skiaBitmap.Info, gcHandle2.AddrOfPinnedObject(), skiaBitmap.RowBytes, (address, context) => { gcHandle2.Free(); }, null);
                    break;

                case VTexFormat.BGRA8888:
                    return TextureDecompressors.ReadBGRA8888(skiaBitmap, GetTextureSpan());

                default:
                    throw new NotImplementedException(string.Format("Unhandled image type: {0}", Format));
            }

            return skiaBitmap;
        }

        public int CalculateTextureDataSize()
        {
            if (Format == VTexFormat.PNG_DXT5 || Format == VTexFormat.PNG_RGBA8888)
            {
                return CalculatePngSize();
            }

            var bytes = 0;

            if (CompressedMips != null)
            {
                bytes = CompressedMips.Sum();
            }
            else
            {
                for (var j = 0; j < NumMipLevels; j++)
                {
                    bytes += CalculateBufferSizeForMipLevel(j) * (Flags.HasFlag(VTexFlags.CUBE_TEXTURE) ? 6 : 1);
                }
            }

            return bytes;
        }

        private int CalculateBufferSizeForMipLevel(int mipLevel)
        {
            var bytesPerPixel = BlockSize;
            var width = MipLevelSize(Width, mipLevel);
            var height = MipLevelSize(Height, mipLevel);
            var depth = MipLevelSize(Depth, mipLevel);

            if (Format == VTexFormat.DXT1
            || Format == VTexFormat.DXT5
            || Format == VTexFormat.BC6H
            || Format == VTexFormat.BC7
            || Format == VTexFormat.ETC2
            || Format == VTexFormat.ETC2_EAC
            || Format == VTexFormat.ATI1N)
            {
                var misalign = width % 4;

                if (misalign > 0)
                {
                    width += 4 - misalign;
                }

                misalign = height % 4;

                if (misalign > 0)
                {
                    height += 4 - misalign;
                }

                if (width < 4 && width > 0)
                {
                    width = 4;
                }

                if (height < 4 && height > 0)
                {
                    height = 4;
                }

                if (depth < 4 && depth > 1)
                {
                    depth = 4;
                }

                var numBlocks = (width * height) >> 4;
                numBlocks *= depth;

                return numBlocks * bytesPerPixel;
            }

            return width * height * depth * bytesPerPixel;
        }

        private void SkipMipmaps()
        {
            if (NumMipLevels < 2)
            {
                return;
            }

            for (var j = NumMipLevels - 1; j > MipmapLevelToExtract; j--)
            {
                int offset;

                if (CompressedMips != null)
                {
                    offset = CompressedMips[j];
                }
                else
                {
                    offset = CalculateBufferSizeForMipLevel(j) * (Flags.HasFlag(VTexFlags.CUBE_TEXTURE) ? 6 : 1);
                }

                Reader.BaseStream.Position += offset;
            }
        }

        private Span<byte> GetTextureSpan(int mipLevel = MipmapLevelToExtract)
        {
            var uncompressedSize = CalculateBufferSizeForMipLevel(mipLevel);
            var output = new Span<byte>(new byte[uncompressedSize]);

            if (!IsActuallyCompressedMips)
            {
                Reader.Read(output);
                return output;
            }

            var compressedSize = CompressedMips[mipLevel];

            if (compressedSize >= uncompressedSize)
            {
                Reader.Read(output);
                return output;
            }

            var input = Reader.ReadBytes(compressedSize);

            LZ4Codec.Decode(input, output);

            return output;
        }

        public byte[] GetDecompressedTextureAtMipLevel(int mipLevel)
        {
            return GetTextureSpan(mipLevel).ToArray();
        }

        private BinaryReader GetDecompressedBuffer()
        {
            if (!IsActuallyCompressedMips)
            {
                return Reader;
            }

            var outStream = new MemoryStream(GetDecompressedTextureAtMipLevel(MipmapLevelToExtract), false);

            return new BinaryReader(outStream); // TODO: dispose
        }

        private int CalculatePngSize()
        {
            var size = 8; // PNG header
            var originalPosition = Reader.BaseStream.Position;

            Reader.BaseStream.Position = DataOffset;

            try
            {
                var pngHeaderA = Reader.ReadInt32();
                var pngHeaderB = Reader.ReadInt32();

                if (pngHeaderA != 0x474E5089)
                {
                    throw new UnexpectedMagicException("This is not PNG", pngHeaderA, nameof(pngHeaderA));
                }

                if (pngHeaderB != 0x0A1A0A0D)
                {
                    throw new UnexpectedMagicException("This is not PNG", pngHeaderB, nameof(pngHeaderB));
                }

                var chunk = 0;

                // Scan all the chunks until IEND
                do
                {
                    // Integers in png are big endian
                    var number = Reader.ReadBytes(sizeof(uint));
                    Array.Reverse(number);
                    size += BitConverter.ToInt32(number);
                    size += 12; // length + chunk type + crc

                    chunk = Reader.ReadInt32();

                    Reader.BaseStream.Position = DataOffset + size;
                }
                while (chunk != 0x444E4549);
            }
            finally
            {
                Reader.BaseStream.Position = originalPosition;
            }

            return size;
        }

        private static int MipLevelSize(int size, int level)
        {
            size >>= level;

            return Math.Max(size, 1);
        }

        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
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

                if (b.Key == VTexExtraData.COMPRESSED_MIP_SIZE)
                {
                    writer.WriteLine("{0,-16}   [ {1} mips, sized: {2} ]", string.Empty, CompressedMips.Length, string.Join(", ", CompressedMips));
                }
                else if (b.Key == VTexExtraData.CUBEMAP_RADIANCE_SH)
                {
                    writer.WriteLine("{0,-16}   [ {1} coefficients: {2} ]", string.Empty, RadianceCoefficients.Length, string.Join(", ", RadianceCoefficients));
                }
                else if (b.Key == VTexExtraData.SHEET)
                {
                    var data = GetSpriteSheetData();

                    writer.WriteLine("{0,-16} {1} Sheet Sequences:", string.Empty, data.Sequences.Length);

                    for (var s = 0; s < data.Sequences.Length; s++)
                    {
                        var sequence = data.Sequences[s];

                        writer.WriteLine("{0,-16} [Sequence {1}]:", string.Empty, s);
                        writer.WriteLine("{0,-16}   m_name            = '{1}'", string.Empty, sequence.Name);
                        writer.WriteLine("{0,-16}   m_bClamp          = {1}", string.Empty, sequence.Clamp);
                        writer.WriteLine("{0,-16}   m_bAlphaCrop      = {1}", string.Empty, sequence.AlphaCrop);
                        writer.WriteLine("{0,-16}   m_bNoColor        = {1}", string.Empty, sequence.NoColor);
                        writer.WriteLine("{0,-16}   m_bNoAlpha        = {1}", string.Empty, sequence.NoAlpha);
                        writer.WriteLine("{0,-16}   m_flTotalTime     = {1:F6}", string.Empty, sequence.FramesPerSecond);
                        writer.WriteLine("{0,-16}   {1} Float Params:", string.Empty, sequence.FloatParams.Count);

                        foreach (var (floatName, floatValue) in sequence.FloatParams)
                        {
                            writer.WriteLine("{0,-16}     '{1}' = {2:F6}", string.Empty, floatName, floatValue);
                        }

                        writer.WriteLine("{0,-16}   {1} Frames:", string.Empty, sequence.Frames.Length);

                        for (var f = 0; f < sequence.Frames.Length; f++)
                        {
                            var frame = sequence.Frames[f];

                            writer.WriteLine("{0,-16}     [Sequence {1} Frame {2}]:", string.Empty, s, f);
                            writer.WriteLine("{0,-16}       m_flDisplayTime  = {1:F6}", string.Empty, frame.DisplayTime);
                            writer.WriteLine("{0,-16}       {1} Images:", string.Empty, frame.Images.Length);

                            for (var i = 0; i < frame.Images.Length; i++)
                            {
                                var image = frame.Images[i];

                                writer.WriteLine("{0,-16}         [{1}.{2}.{3}] uvCropped    = {{ ( {4:F6}, {5:F6} ), ( {6:F6}, {7:F6} ) }}", string.Empty, s, f, i, image.StartMins.X, image.StartMins.Y, image.StartMaxs.X, image.StartMaxs.Y);
                                writer.WriteLine("{0,-16}         [{1}.{2}.{3}] uvUncropped  = {{ ( {4:F6}, {5:F6} ), ( {6:F6}, {7:F6} ) }}", string.Empty, s, f, i, image.EndMins.X, image.EndMins.Y, image.EndMaxs.X, image.EndMaxs.Y);
                            }
                        }
                    }
                }
            }

            for (var j = 0; j < NumMipLevels; j++)
            {
                writer.WriteLine($"Mip level {j} - buffer size: {CalculateBufferSizeForMipLevel(j)}");
            }

            return writer.ToString();
        }
    }
}
