using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// Assembles the six faces of a cube texture into a single 4:3 horizontal cross image, the layout the
    /// texture compiler reads a cubemap input from (every shipped Dota 2 <c>*_cube.tga</c> source uses it).
    /// </summary>
    static class CubemapCrossLayout
    {
        private enum FaceTransform
        {
            Transpose,
            AntiTranspose,
            FlipVertical,
            FlipHorizontal,
        }

        // Indexed by Texture.CubemapFace. Placement and orientation were matched pixel for pixel against
        // shipped cross sources and their compiled textures:
        //
        //         [+Z]
        //   [-X]  [+Y]  [+X]  [-Y]
        //         [-Z]
        private static readonly (int Column, int Row, FaceTransform Transform)[] FaceCells =
        [
            (2, 1, FaceTransform.Transpose),
            (0, 1, FaceTransform.AntiTranspose),
            (1, 1, FaceTransform.FlipVertical),
            (3, 1, FaceTransform.FlipHorizontal),
            (1, 0, FaceTransform.FlipVertical),
            (1, 2, FaceTransform.FlipHorizontal),
        ];

        public static bool CanCreate(Texture texture)
            => (texture.Flags & VTexFlags.CUBE_TEXTURE) != 0 && texture.Depth == 1;

        /// <summary>
        /// Builds the cross from the first mip of each face. High dynamic range faces are decoded to LDR, so the
        /// result can always be saved as PNG.
        /// </summary>
        public static SKBitmap Create(Texture texture)
        {
            if (!CanCreate(texture))
            {
                throw new ArgumentException("Texture is not a single cubemap.", nameof(texture));
            }

            var decodeFlags = texture.IsHighDynamicRange ? TextureCodec.ForceLDR : TextureCodec.Auto;
            var faceSize = texture.ActualWidth;
            var crossWidth = faceSize * 4;
            SKBitmap? crossTemp = null;

            try
            {
                crossTemp = new SKBitmap(crossWidth, faceSize * 3, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                var cross = crossTemp;

                using var crossPixmap = cross.PeekPixels();
                var crossPixels = crossPixmap.GetPixelSpan<uint>();
                crossPixels.Fill((uint)SKColors.Black);

                for (var face = 0; face < 6; face++)
                {
                    using var decoded = texture.GenerateBitmap(face: (Texture.CubemapFace)face, decodeFlags: decodeFlags);
                    using var faceBitmap = decoded.ColorType == SKColorType.Bgra8888 ? null : decoded.Copy(SKColorType.Bgra8888);
                    var source = faceBitmap ?? decoded;

                    if (source.Width != faceSize || source.Height != faceSize)
                    {
                        throw new InvalidOperationException($"Cubemap face {face} is {source.Width}x{source.Height}, expected {faceSize}x{faceSize}.");
                    }

                    using var facePixmap = source.PeekPixels();
                    var facePixels = facePixmap.GetPixelSpan<uint>();

                    var (column, row, transform) = FaceCells[face];
                    var originX = column * faceSize;
                    var originY = row * faceSize;
                    var last = faceSize - 1;

                    for (var y = 0; y < faceSize; y++)
                    {
                        for (var x = 0; x < faceSize; x++)
                        {
                            var (cellX, cellY) = transform switch
                            {
                                FaceTransform.Transpose => (y, x),
                                FaceTransform.AntiTranspose => (last - y, last - x),
                                FaceTransform.FlipVertical => (x, last - y),
                                FaceTransform.FlipHorizontal => (last - x, y),
                                _ => throw new InvalidOperationException($"Unknown face transform {transform}"),
                            };

                            crossPixels[(originY + cellY) * crossWidth + originX + cellX] = facePixels[y * faceSize + x];
                        }
                    }
                }

                crossTemp = null;
                return cross;
            }
            finally
            {
                crossTemp?.Dispose();
            }
        }
    }
}
