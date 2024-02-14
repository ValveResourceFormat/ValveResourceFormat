using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Compression.LZ4;
using SkiaSharp;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.TextureDecoders;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class Texture : ResourceData
    {
        public enum CubemapFace
        {
            PositiveX, // rt
            NegativeX, // lf
            PositiveY, // bk
            NegativeY, // ft
            PositiveZ, // up
            NegativeZ, // dn
        }

        public class SpritesheetData
        {
            public class Sequence
            {
                public class Frame
                {
                    public class Image
                    {
                        public Vector2 CroppedMin { get; set; }
                        public Vector2 CroppedMax { get; set; }

                        public Vector2 UncroppedMin { get; set; }
                        public Vector2 UncroppedMax { get; set; }

                        public SKRectI GetCroppedRect(int width, int height)
                        {
                            var startX = (int)(CroppedMin.X * width);
                            var startY = (int)(CroppedMin.Y * height);
                            var endX = (int)(CroppedMax.X * width);
                            var endY = (int)(CroppedMax.Y * height);

                            return new SKRectI(startX, startY, endX, endY);
                        }

                        public SKRectI GetUncroppedRect(int width, int height)
                        {
                            var startX = (int)(UncroppedMin.X * width);
                            var startY = (int)(UncroppedMin.Y * height);
                            var endX = (int)(UncroppedMax.X * width);
                            var endY = (int)(UncroppedMax.Y * height);

                            return new SKRectI(startX, startY, endX, endY);
                        }
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
                public Dictionary<string, float> FloatParams { get; } = [];
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

        private BinaryReader Reader => Resource.Reader;
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

        // Some textures have displayrect set to 1x1, but that's not the expected size
        // If it's set to 1x1, but the real size does not expand to 4x4 (the usual block compression size), ignore it
        public ushort ActualWidth => NonPow2Width > 0 && (NonPow2Width != 1 || Width == 4) ? NonPow2Width : Width;
        public ushort ActualHeight => NonPow2Height > 0 && (NonPow2Height != 1 || Height == 4) ? NonPow2Height : Height;

        public Texture()
        {
            ExtraData = [];
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            Resource = resource;

            reader.BaseStream.Position = Offset;

            Version = reader.ReadUInt16();

            if (Version != 1)
            {
                throw new UnexpectedMagicException("Unknown vtex version", Version, nameof(Version));
            }

            Flags = (VTexFlags)reader.ReadUInt16();

            Reflectivity =
            [
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
            ];
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

                ExtraData.EnsureCapacity((int)extraDataCount);

                for (var i = 0; i < extraDataCount; i++)
                {
                    var type = (VTexExtraData)reader.ReadUInt32();
                    var offset = reader.ReadUInt32() - 8;
                    var size = reader.ReadUInt32();

                    var prevOffset = reader.BaseStream.Position;

                    reader.BaseStream.Position += offset;
                    ExtraData.Add(type, reader.ReadBytes((int)size));
                    reader.BaseStream.Position -= size;

                    if (type == VTexExtraData.METADATA)
                    {
                        reader.ReadUInt16();
                        var nw = reader.ReadUInt16();
                        var nh = reader.ReadUInt16();
                        if (nw > 0 && nh > 0 && Width >= nw && Height >= nh)
                        {
                            NonPow2Width = nw;
                            NonPow2Height = nh;
                        }
                        /* TODO:
                        [Entry 1: VTEX_EXTRA_DATA_METADATA - 128 bytes ]
                        DisplayRect =[4096  4096]
                        MotionVectorsMaxDistanceInPx = 0
                        RangeMin =[0.00 0.00 0.00 0.00]
                        RangeMax =[0.00 0.00 0.00 0.00]
                        */
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
                                CroppedMin = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                                CroppedMax = new Vector2(reader.ReadSingle(), reader.ReadSingle()),

                                // uvUncropped
                                UncroppedMin = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                                UncroppedMax = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
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

        public bool IsHighDynamicRange => Format is VTexFormat.BC6H
            or VTexFormat.R16
            or VTexFormat.RG1616
            or VTexFormat.RGBA16161616
            or VTexFormat.R16F
            or VTexFormat.RG1616F
            or VTexFormat.RGBA16161616F
            or VTexFormat.R32F
            or VTexFormat.RG3232F
            or VTexFormat.RGB323232F
            or VTexFormat.RGBA32323232F;

        public bool IsRawJpeg => Format is VTexFormat.JPEG_DXT5 or VTexFormat.JPEG_RGBA8888;
        public bool IsRawPng => Format is VTexFormat.PNG_DXT5 or VTexFormat.PNG_RGBA8888;

        internal byte[] ReadRawImageData()
        {
            if (IsRawPng || IsRawJpeg)
            {
                Reader.BaseStream.Position = DataOffset;
                SkipMipmaps(0);
                return Reader.ReadBytes(CalculateTextureDataSize());
            }

            return null;
        }

        /// <summary>
        /// Generate a bitmap for given parameters.
        /// </summary>
        /// <param name="depth">The depth to extract.</param>
        /// <param name="face">The face to extract for cube textures.</param>
        /// <param name="mipLevel">The mip level to extract.</param>
        /// <returns>Skia bitmap.</returns>
        public SKBitmap GenerateBitmap(uint depth = 0, CubemapFace face = 0, uint mipLevel = 0)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(depth, Depth, nameof(depth));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, NumMipLevels, nameof(mipLevel));

            if (face > 0)
            {
                if ((Flags & VTexFlags.CUBE_TEXTURE) == 0)
                {
                    throw new ArgumentException($"This is not a cubemap texture.", nameof(face));
                }

                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((int)face, 6, nameof(face));
            }

            var width = MipLevelSize(ActualWidth, mipLevel);
            var height = MipLevelSize(ActualHeight, mipLevel);

            Reader.BaseStream.Position = DataOffset;

            SkipMipmaps(mipLevel);

            switch (Format)
            {
                // TODO: Are we sure DXT5 and RGBA8888 are just raw buffers?
                case VTexFormat.JPEG_DXT5:
                case VTexFormat.JPEG_RGBA8888:
                    return SKBitmap.Decode(Reader.ReadBytes(CalculateJpegSize()));

                case VTexFormat.PNG_DXT5:
                case VTexFormat.PNG_RGBA8888:
                    return SKBitmap.Decode(Reader.ReadBytes(CalculatePngSize()));
            }

            var decodeFlags = RetrieveCodecFromResourceEditInfo();
            var colorType = IsHighDynamicRange ? SKColorType.RgbaF32 : SKColorType.Bgra8888;

            if (HardwareAcceleratedTextureDecoder.Decoder != null)
            {
                var skiaBitmap2 = new SKBitmap(width, height, colorType, SKAlphaType.Unpremul);

                try
                {
                    if (HardwareAcceleratedTextureDecoder.Decoder.Decode(skiaBitmap2, Resource, depth, face, mipLevel, decodeFlags))
                    {
                        var bitmapToReturn = skiaBitmap2;
                        skiaBitmap2 = null;
                        return bitmapToReturn;
                    }
                }
                finally
                {
                    skiaBitmap2?.Dispose();
                }
            }

            var blockWidth = MipLevelSize(Width, mipLevel);
            var blockHeight = MipLevelSize(Height, mipLevel);

            var skiaBitmap = new SKBitmap(width, height, colorType, SKAlphaType.Unpremul);
            ITextureDecoder decoder = null;

            switch (Format)
            {
                case VTexFormat.DXT1:
                    decoder = new DecodeDXT1(blockWidth, blockHeight);
                    break;

                case VTexFormat.DXT5:
                    decoder = new DecodeDXT5(blockWidth, blockHeight, decodeFlags);
                    break;

                case VTexFormat.I8:
                    decoder = new DecodeI8();
                    break;

                case VTexFormat.RGBA8888:
                    decoder = new DecodeRGBA8888();
                    break;

                case VTexFormat.R16:
                    decoder = new DecodeR16();
                    break;

                case VTexFormat.RG1616:
                    decoder = new DecodeRG1616();
                    break;

                case VTexFormat.RGBA16161616:
                    decoder = new DecodeRGBA16161616();
                    break;

                case VTexFormat.R16F:
                    decoder = new DecodeR16F();
                    break;

                case VTexFormat.RG1616F:
                    decoder = new DecodeRG1616F();
                    break;

                case VTexFormat.RGBA16161616F:
                    decoder = new DecodeRGBA16161616F();
                    break;

                case VTexFormat.R32F:
                    decoder = new DecodeR32F();
                    break;

                case VTexFormat.RG3232F:
                    decoder = new DecodeRG3232F();
                    break;

                case VTexFormat.RGB323232F:
                    decoder = new DecodeRGB323232F();
                    break;

                case VTexFormat.RGBA32323232F:
                    decoder = new DecodeRGBA32323232F();
                    break;

                case VTexFormat.BC6H:
                    decoder = new DecodeBC6H(blockWidth, blockHeight);
                    break;

                case VTexFormat.BC7:
                    decoder = new DecodeBC7(blockWidth, blockHeight, decodeFlags);
                    break;

                case VTexFormat.ATI2N:
                    decoder = new DecodeATI2N(blockWidth, blockHeight, decodeFlags);
                    break;

                case VTexFormat.IA88:
                    decoder = new DecodeIA88();
                    break;

                case VTexFormat.ATI1N:
                    decoder = new DecodeATI1N(blockWidth, blockHeight);
                    break;

                case VTexFormat.ETC2:
                    decoder = new DecodeETC2(blockWidth, blockHeight);
                    break;

                case VTexFormat.ETC2_EAC:
                    decoder = new DecodeETC2EAC(blockWidth, blockHeight);
                    break;

                case VTexFormat.BGRA8888:
                    decoder = new DecodeBGRA8888();
                    break;
            }

            if (decoder == null)
            {
                throw new UnexpectedMagicException("Unhandled image type", (int)Format, nameof(Format));
            }

            var uncompressedSize = CalculateBufferSizeForMipLevel(mipLevel);
            var buf = ArrayPool<byte>.Shared.Rent(uncompressedSize);

            try
            {
                var span = buf.AsSpan(0, uncompressedSize);

                ReadTexture(mipLevel, span);

                if ((Flags & VTexFlags.CUBE_TEXTURE) != 0)
                {
                    var faceSize = uncompressedSize / (6 * Depth);
                    var faceOffset = 0;

                    if (depth > 0)
                    {
                        faceOffset = faceSize * (int)depth * 6;
                    }

                    faceOffset += faceSize * (int)face;

                    span = span[faceOffset..(faceOffset + faceSize)];
                }
                else if (depth > 0)
                {
                    var faceSize = uncompressedSize / Depth;
                    var faceOffset = faceSize * (int)depth;
                    faceOffset += faceSize * (int)face;

                    span = span[faceOffset..(faceOffset + faceSize)];
                }

                decoder.Decode(skiaBitmap, span);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }

            return skiaBitmap;
        }

        public int CalculateTextureDataSize()
        {
            if (IsRawJpeg)
            {
                return CalculateJpegSize();
            }

            if (IsRawPng)
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
                for (var j = 0u; j < NumMipLevels; j++)
                {
                    bytes += CalculateBufferSizeForMipLevel(j);
                }
            }

            return bytes;
        }

        /// <summary>
        /// Calculate buffer size that is required to hold data of specified mip level.
        /// </summary>
        /// <param name="mipLevel">Mip level for which to calculate buffer size.</param>
        /// <returns>Buffer size.</returns>
        public int CalculateBufferSizeForMipLevel(uint mipLevel)
        {
            var bytesPerPixel = BlockSize;
            var width = MipLevelSize(Width, mipLevel);
            var height = MipLevelSize(Height, mipLevel);

            if ((Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                bytesPerPixel *= 6;
            }

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

                var numBlocks = (width * height) >> 4;

                return numBlocks * Depth * bytesPerPixel;
            }

            return width * height * Depth * bytesPerPixel;
        }

        private void SkipMipmaps(uint desiredMipLevel)
        {
            if (NumMipLevels < 2)
            {
                return;
            }

            for (var j = NumMipLevels - 1u; j > desiredMipLevel; j--)
            {
                var size = CalculateBufferSizeForMipLevel(j);

                if (CompressedMips != null)
                {
                    var compressedSize = CompressedMips[j];

                    if (size > compressedSize)
                    {
                        size = compressedSize;
                    }
                }

                Reader.BaseStream.Position += size;
            }
        }

        private void ReadTexture(uint mipLevel, Span<byte> output)
        {
            if (!IsActuallyCompressedMips)
            {
                Reader.Read(output);
                return;
            }

            var compressedSize = CompressedMips[mipLevel];

            if (compressedSize >= output.Length)
            {
                Reader.Read(output);
                return;
            }

            var buf = ArrayPool<byte>.Shared.Rent(compressedSize);

            try
            {
                var span = buf.AsSpan(0, compressedSize);
                Reader.Read(span);
                var written = LZ4Codec.Decode(span, output);

                if (written != output.Length)
                {
                    throw new InvalidDataException($"Failed to decompress LZ4 (expected {output.Length} bytes, got {written}) (texture format is {Format}).");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        /// <summary>
        /// Biggest buffer size to be used with <see cref="GetEveryMipLevelTexture"/>.
        /// </summary>
        public int GetBiggestBufferSize() => CalculateBufferSizeForMipLevel(0);

        /// <summary>
        /// Get every mip level size starting from the smallest one. Used when uploading textures to the GPU.
        /// This writes into the buffer for every mip level, so the buffer must be used before next texture is yielded.
        /// </summary>
        /// <param name="buffer">Buffer to use when yielding textures, it should be size of <see cref="GetBiggestBufferSize"/> or bigger. This buffer is reused for every mip level.</param>
        /// <param name="maxTextureSize">Max size of texture in pixels.</param>
        public IEnumerable<(int Level, int Width, int Height, int BufferSize)> GetEveryMipLevelTexture(byte[] buffer, int minMipLevelAllowed = 0)
        {
            Reader.BaseStream.Position = Offset + Size;

            for (var i = NumMipLevels - 1; i >= 0; i--)
            {
                var width = Width >> i;
                var height = Height >> i;

                if (i < minMipLevelAllowed)
                {
                    break;
                }

                var uncompressedSize = CalculateBufferSizeForMipLevel((uint)i);
                var output = buffer.AsSpan(0, uncompressedSize);

                ReadTexture((uint)i, output);

                yield return (i, width, height, uncompressedSize);
            }
        }

        /// <summary>
        /// Read single mip level of texture. Buffer size must be at least <see cref="CalculateBufferSizeForMipLevel"/>.
        /// </summary>
        /// <param name="output">Buffer that will receive texture data.</param>
        /// <param name="mipLevel">Mip level for which to read texture data.</param>
        public void ReadTextureMipLevel(Span<byte> output, uint mipLevel)
        {
            var bufferSize = CalculateBufferSizeForMipLevel(mipLevel);

            if (bufferSize > output.Length)
            {
                throw new ArgumentException($"Buffer size ({output.Length}) must be at least {bufferSize}, mip level {mipLevel}");
            }

            Reader.BaseStream.Position = Offset + Size;

            SkipMipmaps(mipLevel);

            ReadTexture(mipLevel, output);
        }

        private int CalculateJpegSize()
        {
            return (int)(Reader.BaseStream.Length - DataOffset);
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

        private static int MipLevelSize(int size, uint level)
        {
            size >>= (int)level;

            return Math.Max(size, 1);
        }

        public TextureCodec RetrieveCodecFromResourceEditInfo()
        {
            var codec = TextureCodec.None;

            if (IsRawPng || IsRawJpeg)
            {
                return codec;
            }

            if (Resource.EditInfo == null
            || !Resource.EditInfo.Structs.TryGetValue(ResourceEditInfo.REDIStruct.SpecialDependencies, out var specialDepsRedi))
            {
                return codec;
            }

            var specialDeps = (SpecialDependencies)specialDepsRedi;
            var textureCompilerDependencies = specialDeps.List.Where(dependancy => dependancy.CompilerIdentifier == "CompileTexture");

            foreach (var processorAlgorithm in textureCompilerDependencies)
            {
                codec |= processorAlgorithm.String switch
                {
                    // Image processor algorithms
                    "Texture Compiler Version Image YCoCg Conversion" => TextureCodec.YCoCg,
                    "Texture Compiler Version Image NormalizeNormals" => TextureCodec.NormalizeNormals,

                    // Mipmap processor algorithms
                    "Texture Compiler Version Mip HemiOctIsoRoughness_RG_B" => TextureCodec.HemiOctRB,
                    "Texture Compiler Version Mip HemiOctAnisoRoughness" => TextureCodec.HemiOctRB, // do we lose one of the roughness components? (anisotropic is xy)
                    _ => TextureCodec.None,
                };
            }

            if (Format == VTexFormat.DXT5 && codec.HasFlag(TextureCodec.NormalizeNormals))
            {
                codec |= TextureCodec.Dxt5nm;
            }
            else if (Format == VTexFormat.BC7 && codec.HasFlag(TextureCodec.HemiOctRB)
                                              && codec.HasFlag(TextureCodec.NormalizeNormals))
            {
                codec &= ~TextureCodec.NormalizeNormals;
            }

            if (Format is VTexFormat.BC6H)
            {
                codec |= TextureCodec.ColorSpaceLinear;
            }

            return codec;
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

            {
                var flagIndex = 0;
                var currentFlag = -1;
                var flags = (int)Flags;

                writer.WriteLine("{0,-12} = 0x{1:X8}", "Flags", flags);

                while (flagIndex < flags)
                {
                    var flag = 1 << ++currentFlag;

                    flagIndex += flag;

                    if ((flag & flags) == 0)
                    {
                        continue;
                    }

                    var flagObject = Enum.ToObject(typeof(VTexFlags), flag);

                    if (Enum.IsDefined(typeof(VTexFlags), flagObject))
                    {
                        writer.WriteLine("{0,-12} | 0x{1:X8} = VTEX_FLAG_{2}", string.Empty, flag, (VTexFlags)flag);
                    }
                    else
                    {
                        writer.WriteLine("{0,-12} | 0x{1:X8} = <UNKNOWN>", string.Empty, flag);
                    }
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

                                writer.WriteLine("{0,-16}         [{1}.{2}.{3}] uvCropped    = {{ ( {4:F6}, {5:F6} ), ( {6:F6}, {7:F6} ) }}", string.Empty, s, f, i, image.CroppedMin.X, image.CroppedMin.Y, image.CroppedMax.X, image.CroppedMax.Y);
                                writer.WriteLine("{0,-16}         [{1}.{2}.{3}] uvUncropped  = {{ ( {4:F6}, {5:F6} ), ( {6:F6}, {7:F6} ) }}", string.Empty, s, f, i, image.UncroppedMin.X, image.UncroppedMin.Y, image.UncroppedMax.X, image.UncroppedMax.Y);
                            }
                        }
                    }
                }
            }

            if (!IsRawPng && !IsRawJpeg)
            {
                for (var j = 0u; j < NumMipLevels; j++)
                {
                    writer.WriteLine($"Mip level {j} - buffer size: {CalculateBufferSizeForMipLevel(j)}");
                }
            }

            return writer.ToString();
        }
    }
}
