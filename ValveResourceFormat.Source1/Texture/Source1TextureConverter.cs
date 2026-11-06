using VrfTexture = ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Source1.Texture;

/// <summary>Converts a Source 1 texture to an in-memory Source 2 texture resource.</summary>
public static class Source1TextureConverter
{
    private const int MaxSheetSize = 8192;

    /// <summary>
    /// Builds a texture resource from one frame of a Source 1 texture, with all of its mip levels.
    /// An environment map becomes a cube texture of its six faces.
    /// </summary>
    /// <param name="vtf">The parsed texture.</param>
    /// <param name="frame">The frame of an animated texture.</param>
    /// <param name="decode">Whether to decode every format to pixels, so that none is block compressed.</param>
    /// <returns>A resource whose data block is a <see cref="ResourceTypes.Texture"/>. The caller disposes it.</returns>
    /// <remarks>
    /// Formats that Source 2 stores identically are copied as they are. Every other format is decoded to
    /// <see cref="VTexFormat.BGRA8888"/>, or to <see cref="VTexFormat.RGBA32323232F"/> for floating point textures
    /// that have no matching format.
    /// </remarks>
    internal static Resource Convert(VtfFile vtf, int frame = 0, bool decode = false)
    {
        var isCube = vtf.FaceCount >= 6 && (vtf.Flags & VtfFlags.ENVMAP) != 0;
        var faceCount = isCube ? 6 : 1;
        var mipLevels = (vtf.Flags & VtfFlags.NOMIP) != 0 ? 1 : Math.Max(1, vtf.MipmapCount);
        var flags = isCube ? VTexFlags.CUBE_TEXTURE : 0;

        var format = GetNativeFormat(vtf.Format);
        decode |= format == VTexFormat.UNKNOWN;

        if (decode)
        {
            format = vtf.IsHighDynamicRange ? VTexFormat.RGBA32323232F : VTexFormat.BGRA8888;
        }

        var pixelData = new List<ReadOnlyMemory<byte>>(mipLevels * faceCount);

        for (var mip = 0; mip < mipLevels; mip++)
        {
            for (var face = 0; face < faceCount; face++)
            {
                pixelData.Add(GetMipFace(vtf, mip, frame, face, decode));
            }
        }

        var reflectivity = new Vector4(vtf.Reflectivity, 1f);
        var sheet = vtf.GetSpriteSheetData();

        return VrfTexture.Create((ushort)vtf.Width, (ushort)vtf.Height, format, flags, reflectivity, pixelData, sheet);
    }

    /// <summary>
    /// Builds a BC7 texture resource from the first frame of a Source 1 texture, with all of its mip levels.
    /// </summary>
    /// <param name="vtf">The parsed texture.</param>
    /// <returns>A resource whose data block is a <see cref="ResourceTypes.Texture"/>, or <see langword="null"/> for a high dynamic range texture. The caller disposes it.</returns>
    internal static Resource? ConvertToBc7(VtfFile vtf)
    {
        if (vtf.IsHighDynamicRange)
        {
            return null;
        }

        var mipLevels = (vtf.Flags & VtfFlags.NOMIP) != 0 ? 1 : Math.Max(1, vtf.MipmapCount);
        var pixelData = new List<ReadOnlyMemory<byte>>(mipLevels);

        for (var mip = 0; mip < mipLevels; mip++)
        {
            using var bitmap = vtf.DecodeMip(mip, 0, 0);
            var width = Math.Max(1, vtf.Width >> mip);
            var height = Math.Max(1, vtf.Height >> mip);

            pixelData.Add(Source1Bc7Encoder.Encode(bitmap.GetPixelSpan(), width, height));
        }

        var reflectivity = new Vector4(vtf.Reflectivity, 1f);

        return VrfTexture.Create((ushort)vtf.Width, (ushort)vtf.Height, VTexFormat.BC7, 0, reflectivity, pixelData, null);
    }

    /// <summary>
    /// Gets the grid that <see cref="ConvertSheet"/> lays the frames of an animated texture out in.
    /// </summary>
    /// <returns>The columns and rows, or <see langword="null"/> when the texture has one frame, is an environment map or would be too large.</returns>
    internal static (int Columns, int Rows)? GetSheetGrid(VtfHeader header)
    {
        if (header.Frames < 2 || (header.Flags & VtfFlags.ENVMAP) != 0)
        {
            return null;
        }

        var columns = (int)MathF.Ceiling(MathF.Sqrt(header.Frames));
        var rows = (header.Frames + columns - 1) / columns;

        if (columns * header.Width > MaxSheetSize || rows * header.Height > MaxSheetSize)
        {
            return null;
        }

        return (columns, rows);
    }

    /// <summary>
    /// Builds a texture resource that holds every frame of an animated texture side by side, row by row from the top left.
    /// </summary>
    /// <param name="vtf">The parsed texture.</param>
    /// <returns>
    /// A resource whose data block is a <see cref="ResourceTypes.Texture"/> laid out by <see cref="GetSheetGrid"/>, or
    /// <see langword="null"/> when the texture has no grid. The caller disposes it.
    /// </returns>
    /// <remarks>
    /// Every mip level is the grid of the same mip level of the frames, so the levels stop where a frame is one pixel wide or high.
    /// </remarks>
    internal static Resource? ConvertSheet(VtfFile vtf)
    {
        var grid = GetSheetGrid(vtf.Header);

        if (grid == null)
        {
            return null;
        }

        var (columns, rows) = grid.Value;

        var isHdr = vtf.IsHighDynamicRange;
        var format = isHdr ? VTexFormat.RGBA32323232F : VTexFormat.BGRA8888;
        var bytesPerPixel = isHdr ? 16 : 4;
        var maxMipLevels = Math.Min(BitOperations.TrailingZeroCount(vtf.Width), BitOperations.TrailingZeroCount(vtf.Height)) + 1;
        var mipLevels = (vtf.Flags & VtfFlags.NOMIP) != 0 ? 1 : Math.Clamp(vtf.MipmapCount, 1, maxMipLevels);
        var frameCount = Math.Min(columns * rows, vtf.FrameCount);
        var pixelData = new List<ReadOnlyMemory<byte>>(mipLevels);

        for (var mip = 0; mip < mipLevels; mip++)
        {
            var frameWidth = Math.Max(1, vtf.Width >> mip);
            var frameHeight = Math.Max(1, vtf.Height >> mip);
            var rowBytes = frameWidth * bytesPerPixel;
            var sheetRowBytes = columns * rowBytes;
            var sheetPixels = new byte[sheetRowBytes * rows * frameHeight];

            for (var frame = 0; frame < frameCount; frame++)
            {
                using var bitmap = vtf.DecodeMip(mip, frame, 0);
                var source = bitmap.GetPixelSpan();
                var column = frame % columns;
                var row = frame / columns;

                for (var y = 0; y < frameHeight; y++)
                {
                    var destination = (((row * frameHeight) + y) * sheetRowBytes) + (column * rowBytes);
                    source.Slice(y * rowBytes, rowBytes).CopyTo(sheetPixels.AsSpan(destination, rowBytes));
                }
            }

            pixelData.Add(sheetPixels);
        }

        var reflectivity = new Vector4(vtf.Reflectivity, 1f);

        return VrfTexture.Create((ushort)(vtf.Width * columns), (ushort)(vtf.Height * rows), format, 0, reflectivity, pixelData, null);
    }

    private static VTexFormat GetNativeFormat(VtfFormat format) => format switch
    {
        VtfFormat.DXT1 => VTexFormat.DXT1,
        VtfFormat.DXT5 => VTexFormat.DXT5,
        VtfFormat.BC7 => VTexFormat.BC7,
        VtfFormat.RGBA8888 => VTexFormat.RGBA8888,
        VtfFormat.BGRA8888 => VTexFormat.BGRA8888,
        VtfFormat.RGBA16161616F => VTexFormat.RGBA16161616F,
        VtfFormat.RGBA32323232F => VTexFormat.RGBA32323232F,
        _ => VTexFormat.UNKNOWN,
    };

    private static byte[] GetMipFace(VtfFile vtf, int mip, int frame, int face, bool decode)
    {
        if (!decode)
        {
            return vtf.GetHiResPixelData(mip, frame, face).ToArray();
        }

        using var bitmap = vtf.DecodeMip(mip, frame, face);
        return bitmap.GetPixelSpan().ToArray();
    }
}
