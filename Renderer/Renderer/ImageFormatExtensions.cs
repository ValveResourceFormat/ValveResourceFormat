
namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Facts about an <see cref="ImageFormat"/> that hold whatever graphics API is in use.
    /// The mapping onto an API's own format enums lives with that API's mapper instead.
    /// </summary>
    public static class ImageFormatExtensions
    {
        /// <summary>
        /// Returns whether the format is block compressed.
        /// Block compressed data uploads with the compressed image calls and has no pixel format or type.
        /// </summary>
        public static bool IsBlockCompressed(this ImageFormat format) => format
            is ImageFormat.DXT1
            or ImageFormat.DXT1_ONEBITALPHA
            or ImageFormat.DXT3
            or ImageFormat.DXT5
            or ImageFormat.DXT5_NM
            or ImageFormat.ATI1N
            or ImageFormat.ATI2N
            or ImageFormat.BC6H
            or ImageFormat.BC7
            or ImageFormat.R8G8B8_ETC2
            or ImageFormat.R8G8B8A8_ETC2_EAC
            or ImageFormat.R11_EAC
            or ImageFormat.RG11_EAC;

        /// <summary>Returns whether the format has an sRGB storage variant.</summary>
        public static bool HasSrgbVariant(this ImageFormat format) => format
            is ImageFormat.RGBA8888
            or ImageFormat.BGRA8888
            or ImageFormat.DXT1
            or ImageFormat.DXT1_ONEBITALPHA
            or ImageFormat.DXT3
            or ImageFormat.DXT5
            or ImageFormat.BC7
            or ImageFormat.R8G8B8_ETC2
            or ImageFormat.R8G8B8A8_ETC2_EAC;
    }
}
