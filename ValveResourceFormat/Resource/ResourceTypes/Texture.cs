using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using K4os.Compression.LZ4;
using SkiaSharp;
using ValveResourceFormat.TextureDecoders;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a texture resource containing image data with various formats and metadata.
    /// </summary>
    public class Texture : Block
    {
        /// <summary>
        /// Defines the six faces of a cubemap texture.
        /// </summary>
        public enum CubemapFace
        {
            /// <summary>Right face.</summary>
            PositiveX,
            /// <summary>Left face.</summary>
            NegativeX,
            /// <summary>Back face.</summary>
            PositiveY,
            /// <summary>Front face.</summary>
            NegativeY,
            /// <summary>Up face.</summary>
            PositiveZ,
            /// <summary>Down face.</summary>
            NegativeZ,
        }

        /// <summary>
        /// Contains data for sprite sheet animations and sequences.
        /// </summary>
        public class SpritesheetData
        {
            /// <summary>
            /// Represents an animation sequence within a sprite sheet.
            /// </summary>
            public class Sequence
            {
                /// <summary>
                /// Represents a single frame within an animation sequence.
                /// </summary>
                public class Frame
                {
                    /// <summary>
                    /// Represents an image within a frame, defining cropped and uncropped UV coordinates.
                    /// </summary>
                    public class Image
                    {
                        /// <summary>
                        /// Gets or sets the minimum UV coordinates for the cropped image area.
                        /// </summary>
                        public Vector2 CroppedMin { get; set; }

                        /// <summary>
                        /// Gets or sets the maximum UV coordinates for the cropped image area.
                        /// </summary>
                        public Vector2 CroppedMax { get; set; }

                        /// <summary>
                        /// Gets or sets the minimum UV coordinates for the uncropped image area.
                        /// </summary>
                        public Vector2 UncroppedMin { get; set; }

                        /// <summary>
                        /// Gets or sets the maximum UV coordinates for the uncropped image area.
                        /// </summary>
                        public Vector2 UncroppedMax { get; set; }

                        /// <summary>
                        /// Gets the cropped rectangle in pixel coordinates for the specified dimensions.
                        /// </summary>
                        /// <param name="width">The width of the texture in pixels.</param>
                        /// <param name="height">The height of the texture in pixels.</param>
                        /// <returns>A rectangle representing the cropped area in pixel coordinates.</returns>
                        public SKRectI GetCroppedRect(int width, int height)
                        {
                            var startX = (int)(CroppedMin.X * width);
                            var startY = (int)(CroppedMin.Y * height);
                            var endX = (int)(CroppedMax.X * width);
                            var endY = (int)(CroppedMax.Y * height);

                            return new SKRectI(startX, startY, endX, endY);
                        }

                        /// <summary>
                        /// Gets the uncropped rectangle in pixel coordinates for the specified dimensions.
                        /// </summary>
                        /// <param name="width">The width of the texture in pixels.</param>
                        /// <param name="height">The height of the texture in pixels.</param>
                        /// <returns>A rectangle representing the uncropped area in pixel coordinates.</returns>
                        public SKRectI GetUncroppedRect(int width, int height)
                        {
                            var startX = (int)(UncroppedMin.X * width);
                            var startY = (int)(UncroppedMin.Y * height);
                            var endX = (int)(UncroppedMax.X * width);
                            var endY = (int)(UncroppedMax.Y * height);

                            return new SKRectI(startX, startY, endX, endY);
                        }
                    }

                    /// <summary>
                    /// Gets or sets the array of images contained in this frame.
                    /// </summary>
                    public Image[] Images { get; set; }

                    /// <summary>
                    /// Gets or sets the display time for this frame in seconds.
                    /// </summary>
                    public float DisplayTime { get; set; }
                }

                /// <summary>
                /// Gets or sets the array of frames in this sequence.
                /// </summary>
                public Frame[] Frames { get; set; }

                /// <summary>
                /// Gets or sets the playback rate of this sequence in frames per second.
                /// </summary>
                public float FramesPerSecond { get; set; }

                /// <summary>
                /// Gets or sets the name of this sequence.
                /// </summary>
                public string Name { get; set; }

                /// <summary>
                /// Gets or sets a value indicating whether this sequence should clamp at the end.
                /// </summary>
                public bool Clamp { get; set; }

                /// <summary>
                /// Gets or sets a value indicating whether alpha cropping is enabled for this sequence.
                /// </summary>
                public bool AlphaCrop { get; set; }

                /// <summary>
                /// Gets or sets a value indicating whether color information should be ignored.
                /// </summary>
                public bool NoColor { get; set; }

                /// <summary>
                /// Gets or sets a value indicating whether alpha information should be ignored.
                /// </summary>
                public bool NoAlpha { get; set; }

                /// <summary>
                /// Gets the dictionary of floating-point parameters associated with this sequence.
                /// </summary>
                public Dictionary<string, float> FloatParams { get; } = [];
            }

            /// <summary>
            /// Gets or sets the array of animation sequences in this sprite sheet.
            /// </summary>
            public Sequence[] Sequences { get; set; }
        }

        /// <summary>
        /// Gets the block size in bytes for the current texture format.
        /// </summary>
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

        /// <summary>
        /// Gets the block type, which is always DATA for texture blocks.
        /// </summary>
        public override BlockType Type => BlockType.DATA;

        private BinaryReader Reader => Resource.Reader;
        private long DataOffset => Offset + Size;

        /// <summary>
        /// Gets the texture version number.
        /// </summary>
        public ushort Version { get; private set; }

        /// <summary>
        /// Gets the width of the texture in pixels.
        /// </summary>
        public ushort Width { get; private set; }

        /// <summary>
        /// Gets the height of the texture in pixels.
        /// </summary>
        public ushort Height { get; private set; }

        /// <summary>
        /// Gets the depth of the texture for volume textures, or the number of array slices.
        /// </summary>
        public ushort Depth { get; private set; }

        /// <summary>
        /// Gets the reflectivity values as a 4-component vector (RGBA).
        /// </summary>
        public float[] Reflectivity { get; private set; }

        /// <summary>
        /// Gets the texture flags that define various properties and behaviors.
        /// </summary>
        public VTexFlags Flags { get; private set; }

        /// <summary>
        /// Gets the pixel format of the texture data.
        /// </summary>
        public VTexFormat Format { get; private set; }

        /// <summary>
        /// Gets the number of mip levels in the texture.
        /// </summary>
        public byte NumMipLevels { get; private set; }

        /// <summary>
        /// Gets the picmip 0 resolution value.
        /// </summary>
        public uint Picmip0Res { get; private set; }

        /// <summary>
        /// Gets the dictionary containing extra data blocks associated with the texture.
        /// </summary>
        public Dictionary<VTexExtraData, byte[]> ExtraData { get; private set; }

        /// <summary>
        /// Gets the non-power-of-2 width value, if different from the main width.
        /// </summary>
        public ushort NonPow2Width { get; private set; }

        /// <summary>
        /// Gets the non-power-of-2 height value, if different from the main height.
        /// </summary>
        public ushort NonPow2Height { get; private set; }

        private int[] CompressedMips;
        private bool IsActuallyCompressedMips;

        private float[] RadianceCoefficients;

        /// <summary>
        /// Gets the actual width of the texture, using NonPow2Width if available and valid, otherwise Width.
        /// Some textures have displayrect set to 1x1, but that's not the expected size.
        /// If it's set to 1x1, but the real size does not expand to 4x4 (the usual block compression size), it's ignored.
        /// </summary>
        public ushort ActualWidth => NonPow2Width > 0 && (NonPow2Width != 1 || Width == 4) ? NonPow2Width : Width;

        /// <summary>
        /// Gets the actual height of the texture, using NonPow2Height if available and valid, otherwise Height.
        /// Some textures have displayrect set to 1x1, but that's not the expected size.
        /// If it's set to 1x1, but the real size does not expand to 4x4 (the usual block compression size), it's ignored.
        /// </summary>
        public ushort ActualHeight => NonPow2Height > 0 && (NonPow2Height != 1 || Height == 4) ? NonPow2Height : Height;

        /// <summary>
        /// Initializes a new instance of the <see cref="Texture"/> class.
        /// </summary>
        public Texture()
        {
            ExtraData = [];
        }

        /// <summary>
        /// Reads the texture data from the specified binary reader.
        /// </summary>
        /// <param name="reader">The binary reader to read from.</param>
        public override void Read(BinaryReader reader)
        {
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
        }

        /// <summary>
        /// Retrieves sprite sheet data if available in the texture's extra data.
        /// </summary>
        /// <returns>The sprite sheet data, or null if not available.</returns>
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

        /// <summary>
        /// Gets a value indicating whether this texture uses a high dynamic range format.
        /// </summary>
        public bool IsHighDynamicRange => Format
            is VTexFormat.R16
            or VTexFormat.RG1616
            or VTexFormat.RGBA16161616
            or VTexFormat.R16F
            or VTexFormat.RG1616F
            or VTexFormat.RGBA16161616F
            or VTexFormat.R32F
            or VTexFormat.RG3232F
            or VTexFormat.RGB323232F
            or VTexFormat.RGBA32323232F
            or VTexFormat.BC6H;

        /// <summary>
        /// Gets a value indicating whether this texture contains raw image data, not block data.
        /// </summary>
        public bool IsRawAnyImage => IsRawJpeg || IsRawPng || IsRawWebp;

        /// <summary>
        /// Gets a value indicating whether this texture contains raw JPEG data.
        /// </summary>
        public bool IsRawJpeg => Format is VTexFormat.JPEG_DXT5 or VTexFormat.JPEG_RGBA8888;

        /// <summary>
        /// Gets a value indicating whether this texture contains raw PNG data.
        /// </summary>
        public bool IsRawPng => Format is VTexFormat.PNG_DXT5 or VTexFormat.PNG_RGBA8888;

        /// <summary>
        /// Gets a value indicating whether this texture contains raw WebP data.
        /// </summary>
        public bool IsRawWebp => Format is VTexFormat.WEBP_DXT5 or VTexFormat.WEBP_RGBA8888;

        /// <summary>
        /// Reads the raw image data for PNG, JPEG, or WebP textures.
        /// </summary>
        /// <returns>The raw image data as a byte array, or null if not a raw image format.</returns>
        internal byte[] ReadRawImageData()
        {
            if (IsRawAnyImage)
            {
                Reader.BaseStream.Position = DataOffset;
                SkipMipmaps(0);
                return Reader.ReadBytes(CalculateTextureDataSize());
            }

            return null;
        }

        /// <summary>
        /// The default color type used for standard dynamic range bitmaps.
        /// </summary>
        public const SKColorType DefaultBitmapColorType = SKColorType.Bgra8888;

        /// <summary>
        /// The color type used for high dynamic range bitmaps.
        /// </summary>
        public const SKColorType HdrBitmapColorType = SKColorType.RgbaF32;

        /// <summary>
        /// Generate a bitmap for given parameters.
        /// </summary>
        /// <param name="depth">The depth to extract.</param>
        /// <param name="face">The face to extract for cube textures.</param>
        /// <param name="mipLevel">The mip level to extract.</param>
        /// <returns>Skia bitmap.</returns>
        public SKBitmap GenerateBitmap(uint depth = 0, CubemapFace face = 0, uint mipLevel = 0, TextureCodec decodeFlags = TextureCodec.Auto)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, NumMipLevels, nameof(mipLevel));

            var depthMip = (Flags & VTexFlags.VOLUME_TEXTURE) == 0 ? Depth : MipLevelSize(Depth, mipLevel);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(depth, (uint)depthMip, nameof(depth));

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

            switch (Format)
            {
                case VTexFormat.JPEG_DXT5:
                case VTexFormat.JPEG_RGBA8888:
                    Reader.BaseStream.Position = DataOffset;
                    return SKBitmap.Decode(Reader.ReadBytes(CalculateJpegSize()));

                case VTexFormat.PNG_DXT5:
                case VTexFormat.PNG_RGBA8888:
                    Reader.BaseStream.Position = DataOffset;
                    return SKBitmap.Decode(Reader.ReadBytes(CalculatePngSize()));

                case VTexFormat.WEBP_DXT5:
                case VTexFormat.WEBP_RGBA8888:
                    Reader.BaseStream.Position = DataOffset;
                    return SKBitmap.Decode(Reader.ReadBytes(CalculateWebpSize()));
            }

            decodeFlags = decodeFlags == TextureCodec.Auto
                ? RetrieveCodecFromResourceEditInfo()
                : decodeFlags;

            var colorType = IsHighDynamicRange && !decodeFlags.HasFlag(TextureCodec.ForceLDR)
                ? HdrBitmapColorType
                : DefaultBitmapColorType;

            var skiaBitmap = new SKBitmap(width, height, colorType, SKAlphaType.Unpremul);

            /// GPU decoder calls into <see cref="GetEveryMipLevelTexture"/> which sets the reader offset on its own
            if (HardwareAcceleratedTextureDecoder.Decoder?.Decode(skiaBitmap, Resource, depth, face, mipLevel, decodeFlags) == true)
            {
                return skiaBitmap;
            }

            var uncompressedSize = CalculateBufferSizeForMipLevel(mipLevel);
            var buf = ArrayPool<byte>.Shared.Rent(uncompressedSize);

            try
            {
                var span = buf.AsSpan(0, uncompressedSize);

                Reader.BaseStream.Position = DataOffset;
                SkipMipmaps(mipLevel);
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
                    var faceSize = uncompressedSize / depthMip;
                    var faceOffset = faceSize * (int)depth;
                    faceOffset += faceSize * (int)face;

                    span = span[faceOffset..(faceOffset + faceSize)];
                }

                var decoder = CreateDecoder(mipLevel);
                decoder.Decode(skiaBitmap, span);

                Common.ApplyTextureConversions(skiaBitmap, decodeFlags);

                var bitmapToReturn = skiaBitmap;
                skiaBitmap = null;
                return bitmapToReturn;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
                skiaBitmap?.Dispose();
            }
        }

        private ITextureDecoder CreateDecoder(uint mipLevel)
        {
            var blockWidth = MipLevelSize(Width, mipLevel);
            var blockHeight = MipLevelSize(Height, mipLevel);

            return Format switch
            {
                // BCn
                VTexFormat.DXT1 => new DecodeBCn(blockWidth, blockHeight, TinyBCSharp.BlockFormat.BC1NoAlpha),
                VTexFormat.DXT5 => new DecodeBCn(blockWidth, blockHeight, TinyBCSharp.BlockFormat.BC3),
                VTexFormat.ATI1N => new DecodeBCn(blockWidth, blockHeight, TinyBCSharp.BlockFormat.BC4U),
                VTexFormat.ATI2N => new DecodeBCn(blockWidth, blockHeight, TinyBCSharp.BlockFormat.BC5U),
                VTexFormat.BC6H => new DecodeBCn(blockWidth, blockHeight, TinyBCSharp.BlockFormat.BC6HUf32),
                VTexFormat.BC7 => new DecodeBCn(blockWidth, blockHeight, TinyBCSharp.BlockFormat.BC7),

                // ETC
                VTexFormat.ETC2 => new DecodeETC2(blockWidth, blockHeight),
                VTexFormat.ETC2_EAC => new DecodeETC2EAC(blockWidth, blockHeight),

                // Simple colors
                VTexFormat.I8 => new DecodeI8(),
                VTexFormat.RGBA8888 => new DecodeRGBA8888(),
                VTexFormat.R16 => new DecodeR16(),
                VTexFormat.RG1616 => new DecodeRG1616(),
                VTexFormat.RGBA16161616 => new DecodeRGBA16161616(),
                VTexFormat.R16F => new DecodeR16F(),
                VTexFormat.RG1616F => new DecodeRG1616F(),
                VTexFormat.RGBA16161616F => new DecodeRGBA16161616F(),
                VTexFormat.R32F => new DecodeR32F(),
                VTexFormat.RG3232F => new DecodeRG3232F(),
                VTexFormat.RGB323232F => new DecodeRGB323232F(),
                VTexFormat.RGBA32323232F => new DecodeRGBA32323232F(),
                VTexFormat.IA88 => new DecodeIA88(),
                VTexFormat.BGRA8888 => new DecodeBGRA8888(),
                _ => throw new UnexpectedMagicException("Unhandled image type", (int)Format, nameof(Format))
            };
        }

        /// <summary>
        /// Calculates the total size of texture data across all mip levels.
        /// </summary>
        /// <returns>The total size in bytes.</returns>
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

            if (IsRawWebp)
            {
                return CalculateWebpSize();
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
            var (width, height, depth) = CalculateTextureSizesForMipLevel(mipLevel);

            return CalculateBufferSizeForMipLevel(width, height, depth);
        }

        private (int Width, int Height, int Depth) CalculateTextureSizesForMipLevel(uint mipLevel)
        {
            var width = MipLevelSize(Width, mipLevel);
            var height = MipLevelSize(Height, mipLevel);
            var depth = (Flags & VTexFlags.VOLUME_TEXTURE) == 0 ? Depth : MipLevelSize(Depth, mipLevel);

            if ((Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                depth *= 6;
            }

            return (width, height, depth);
        }

        private int CalculateBufferSizeForMipLevel(int width, int height, int depth)
        {
            var bytesPerPixel = BlockSize;

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

                return numBlocks * depth * bytesPerPixel;
            }

            return width * height * depth * bytesPerPixel;
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
        /// <returns>The size of the largest mip level (mip 0) in bytes.</returns>
        public int GetBiggestBufferSize() => CalculateBufferSizeForMipLevel(0);

        /// <summary>
        /// Get every mip level size starting from the smallest one. Used when uploading textures to the GPU.
        /// This writes into the buffer for every mip level, so the buffer must be used before next texture is yielded.
        /// </summary>
        /// <param name="buffer">Buffer to use when yielding textures, it should be size of <see cref="GetBiggestBufferSize"/> or bigger. This buffer is reused for every mip level.</param>
        /// <param name="maxTextureSize">Max size of texture in pixels.</param>
        public IEnumerable<(uint Level, int Width, int Height, int Depth, int BufferSize)> GetEveryMipLevelTexture(byte[] buffer, int minMipLevelAllowed = 0)
        {
            Reader.BaseStream.Position = DataOffset;

            for (var i = NumMipLevels - 1; i >= 0; i--)
            {
                var mipLevel = (uint)i;

                if (mipLevel < minMipLevelAllowed)
                {
                    break;
                }

                var (width, height, depth) = CalculateTextureSizesForMipLevel(mipLevel);
                var uncompressedSize = CalculateBufferSizeForMipLevel(width, height, depth);
                var output = buffer.AsSpan(0, uncompressedSize);

                ReadTexture(mipLevel, output);

                yield return (mipLevel, width, height, depth, uncompressedSize);
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

            Reader.BaseStream.Position = DataOffset;

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

        private int CalculateWebpSize()
        {
            var originalPosition = Reader.BaseStream.Position;

            Reader.BaseStream.Position = DataOffset;

            try
            {
                var riffHeader = Reader.ReadInt32();
                if (riffHeader != 0x46464952) // "RIFF"
                {
                    throw new UnexpectedMagicException("This is not WebP RIFF", riffHeader, nameof(riffHeader));
                }

                var fileSize = Reader.ReadUInt32();
                var webpHeader = Reader.ReadInt32();

                if (webpHeader != 0x50424557) // "WEBP"
                {
                    throw new UnexpectedMagicException("This is not WebP", webpHeader, nameof(webpHeader));
                }

                return (int)(8 + fileSize);
            }
            finally
            {
                Reader.BaseStream.Position = originalPosition;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MipLevelSize(int size, uint level)
        {
            return Math.Max(size >> (int)level, 1);
        }

        /// <summary>
        /// Retrieves the texture codec information from the resource's edit info.
        /// </summary>
        /// <returns>The texture codec flags derived from the edit info.</returns>
        public TextureCodec RetrieveCodecFromResourceEditInfo()
        {
            var codec = TextureCodec.None;

            if (IsRawAnyImage)
            {
                return codec;
            }

            if (Resource.EditInfo == null)
            {
                return codec;
            }

            var textureCompilerDependencies = Resource.EditInfo.SpecialDependencies
                .Where(static dependency => dependency.CompilerIdentifier == "CompileTexture");

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

            if (IsHighDynamicRange)
            {
                codec |= TextureCodec.ColorSpaceLinear;
            }

            return codec;
        }

        /// <summary>
        /// Serializes the texture block to the specified stream.
        /// </summary>
        /// <param name="stream">The stream to serialize to.</param>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <summary>
        /// Writes a text representation of the texture to the specified writer.
        /// </summary>
        /// <param name="writer">The writer to output the text representation to.</param>
        public override void WriteText(IndentedTextWriter writer)
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

            if (!IsRawAnyImage)
            {
                for (var j = 0u; j < NumMipLevels; j++)
                {
                    writer.WriteLine($"Mip level {j} - buffer size: {CalculateBufferSizeForMipLevel(j)}");
                }
            }
        }
    }
}
