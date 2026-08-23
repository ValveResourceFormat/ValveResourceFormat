using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Maps <see cref="ImageFormat"/>, the engine format vocabulary, to the OpenGL equivalents.
    ///
    /// This makes the renderer unaware of graphics API specific formats, and we can later map these to anything else.
    /// The facts that hold for every API live on <see cref="Utils.ImageFormatExtensions"/> instead.
    /// </summary>
    public static class GLImageFormatExtensions
    {
        /// <summary>Returns the sized internal format for texture storage.</summary>
        public static SizedInternalFormat ToGLSizedInternalFormat(this ImageFormat format, bool srgb = false) => (format, srgb) switch
        {
            (ImageFormat.RGBA8888, false) => SizedInternalFormat.Rgba8,
            (ImageFormat.RGBA8888, true) => SizedInternalFormat.Srgb8Alpha8,
            // GL has no BGRA sized internal format; the component order is expressed at upload time instead.
            (ImageFormat.BGRA8888, false) => SizedInternalFormat.Rgba8,
            (ImageFormat.BGRA8888, true) => SizedInternalFormat.Srgb8Alpha8,
            (ImageFormat.DXT1 or ImageFormat.DXT1_ONEBITALPHA, false) => (SizedInternalFormat)InternalFormat.CompressedRgbaS3tcDxt1Ext,
            (ImageFormat.DXT1 or ImageFormat.DXT1_ONEBITALPHA, true) => (SizedInternalFormat)InternalFormat.CompressedSrgbAlphaS3tcDxt1Ext,
            (ImageFormat.DXT3, false) => (SizedInternalFormat)InternalFormat.CompressedRgbaS3tcDxt3Ext,
            (ImageFormat.DXT3, true) => (SizedInternalFormat)InternalFormat.CompressedSrgbAlphaS3tcDxt3Ext,
            (ImageFormat.DXT5 or ImageFormat.DXT5_NM, false) => (SizedInternalFormat)InternalFormat.CompressedRgbaS3tcDxt5Ext,
            (ImageFormat.DXT5, true) => (SizedInternalFormat)InternalFormat.CompressedSrgbAlphaS3tcDxt5Ext,
            (ImageFormat.BC7, false) => (SizedInternalFormat)InternalFormat.CompressedRgbaBptcUnorm,
            (ImageFormat.BC7, true) => (SizedInternalFormat)InternalFormat.CompressedSrgbAlphaBptcUnorm,
            (ImageFormat.R8G8B8_ETC2, false) => (SizedInternalFormat)InternalFormat.CompressedRgb8Etc2,
            (ImageFormat.R8G8B8_ETC2, true) => (SizedInternalFormat)InternalFormat.CompressedSrgb8Etc2,
            (ImageFormat.R8G8B8A8_ETC2_EAC, false) => (SizedInternalFormat)InternalFormat.CompressedRgba8Etc2Eac,
            (ImageFormat.R8G8B8A8_ETC2_EAC, true) => (SizedInternalFormat)InternalFormat.CompressedSrgb8Alpha8Etc2Eac,

            (_, true) => throw new NotImplementedException($"Format {format} has no sRGB variant"),

            (ImageFormat.I8, _) => SizedInternalFormat.R8,
            (ImageFormat.R16, _) => SizedInternalFormat.R16,
            (ImageFormat.RG1616, _) => SizedInternalFormat.Rg16,
            (ImageFormat.RGBA16161616, _) => SizedInternalFormat.Rgba16,
            (ImageFormat.R16F, _) => SizedInternalFormat.R16f,
            (ImageFormat.RG1616F, _) => SizedInternalFormat.Rg16f,
            (ImageFormat.RGBA16161616F, _) => SizedInternalFormat.Rgba16f,
            (ImageFormat.IMAGE_FORMAT_R11G11B10_FLOAT, _) => SizedInternalFormat.R11fG11fB10f,
            (ImageFormat.R32F, _) => SizedInternalFormat.R32f,
            (ImageFormat.RG3232F, _) => SizedInternalFormat.Rg32f,
            (ImageFormat.RGBA32323232F, _) => SizedInternalFormat.Rgba32f,
            (ImageFormat.R32_UINT, _) => SizedInternalFormat.R32ui,
            (ImageFormat.RGBA32323232_UINT, _) => SizedInternalFormat.Rgba32ui,
            (ImageFormat.ATI1N, _) => (SizedInternalFormat)InternalFormat.CompressedRedRgtc1,
            (ImageFormat.ATI2N, _) => (SizedInternalFormat)InternalFormat.CompressedRgRgtc2,
            (ImageFormat.BC6H, _) => (SizedInternalFormat)InternalFormat.CompressedRgbBptcUnsignedFloat,
            (ImageFormat.R11_EAC, _) => (SizedInternalFormat)InternalFormat.CompressedR11Eac,
            (ImageFormat.RG11_EAC, _) => (SizedInternalFormat)InternalFormat.CompressedRg11Eac,
            (ImageFormat.D16, _) => (SizedInternalFormat)PixelInternalFormat.DepthComponent16,
            (ImageFormat.D32, _) => (SizedInternalFormat)PixelInternalFormat.DepthComponent32f,
            (ImageFormat.D32FS8, _) => (SizedInternalFormat)PixelInternalFormat.Depth32fStencil8,
            _ => throw new NotImplementedException($"Unsupported format {format}"),
        };

        /// <summary>Returns the pixel format for uploads and readbacks. Not valid for block compressed formats.</summary>
        public static PixelFormat ToGLPixelFormat(this ImageFormat format) => format switch
        {
            ImageFormat.RGBA8888 => PixelFormat.Rgba,
            ImageFormat.BGRA8888 => PixelFormat.Bgra,
            ImageFormat.I8 => PixelFormat.Red,
            ImageFormat.R16 => PixelFormat.Red,
            ImageFormat.RG1616 => PixelFormat.Rg,
            ImageFormat.RGBA16161616 => PixelFormat.Rgba,
            ImageFormat.R16F => PixelFormat.Red,
            ImageFormat.RG1616F => PixelFormat.Rg,
            ImageFormat.RGBA16161616F => PixelFormat.Rgba,
            ImageFormat.IMAGE_FORMAT_R11G11B10_FLOAT => PixelFormat.Rgb,
            ImageFormat.R32F => PixelFormat.Red,
            ImageFormat.RG3232F => PixelFormat.Rg,
            ImageFormat.RGBA32323232F => PixelFormat.Rgba,
            ImageFormat.R32_UINT => PixelFormat.RedInteger,
            ImageFormat.RGBA32323232_UINT => PixelFormat.RgbaInteger,
            ImageFormat.D16 => PixelFormat.DepthComponent,
            ImageFormat.D32 => PixelFormat.DepthComponent,
            ImageFormat.D32FS8 => PixelFormat.DepthStencil,
            _ => throw new NotImplementedException($"Unsupported format {format}"),
        };

        /// <summary>Returns the pixel component type for uploads and readbacks. Not valid for block compressed formats.</summary>
        public static PixelType ToGLPixelType(this ImageFormat format) => format switch
        {
            ImageFormat.RGBA8888 => PixelType.UnsignedByte,
            ImageFormat.BGRA8888 => PixelType.UnsignedByte,
            ImageFormat.I8 => PixelType.UnsignedByte,
            ImageFormat.R16 => PixelType.UnsignedShort,
            ImageFormat.RG1616 => PixelType.UnsignedShort,
            ImageFormat.RGBA16161616 => PixelType.UnsignedShort,
            ImageFormat.R16F => PixelType.HalfFloat,
            ImageFormat.RG1616F => PixelType.HalfFloat,
            ImageFormat.RGBA16161616F => PixelType.HalfFloat,
            ImageFormat.IMAGE_FORMAT_R11G11B10_FLOAT => PixelType.UnsignedInt10F11F11FRev,
            ImageFormat.R32F => PixelType.Float,
            ImageFormat.RG3232F => PixelType.Float,
            ImageFormat.RGBA32323232F => PixelType.Float,
            ImageFormat.R32_UINT => PixelType.UnsignedInt,
            ImageFormat.RGBA32323232_UINT => PixelType.UnsignedInt,
            ImageFormat.D16 => PixelType.UnsignedShort,
            ImageFormat.D32 => PixelType.Float,
            ImageFormat.D32FS8 => PixelType.Float32UnsignedInt248Rev,
            _ => throw new NotImplementedException($"Unsupported format {format}"),
        };
    }
}
