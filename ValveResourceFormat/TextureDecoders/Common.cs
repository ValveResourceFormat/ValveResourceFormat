using System.Buffers.Binary;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Color
    {
        public byte b;
        public byte g;
        public byte r;
        public byte a;

        /*public static implicit operator Color(Span<byte> data)
        {
            return new Color { b = data[0], g = data[1], r = data[2], a = data[3] };
        }*/
    }

    /// <summary>
    /// Texture codec flags for decoding and color space conversions.
    /// </summary>
    [Flags]
    public enum TextureCodec
    {
        /// <summary>
        /// No codec flags.
        /// </summary>
        None = 0,

        /// <summary>
        /// Co, Cg, Scale, and Y stored in RGBA respectively.
        /// </summary>
        YCoCg = 1 << 0,

        /// <summary>
        /// HDR content stored in 8 bit by muliplying colors with alpha
        /// and another constant (typically 16)
        /// </summary>
        RGBM = 1 << 1, // todo

        /// <summary>
        /// Hemi Octahedron surface normal data in RB. Widens to RGB.
        /// </summary>
        HemiOctRB = 1 << 2,

        /// <summary>
        /// Reconstruct normal Z from X and Y.
        /// </summary>
        NormalizeNormals = 1 << 3,

        /// <summary>
        ///  Swizzle red with alpha (exploiting higher bitcount alpha in dxt5)
        /// </summary>
        Dxt5nm = 1 << 4,

        /// <summary>
        /// Indicates the texture data is stored in linear color space.
        /// </summary>
        ColorSpaceLinear = 1 << 5,

        /// <summary>
        /// Indicates the texture data is stored in sRGB gamma space.
        /// </summary>
        ColorSpaceSrgb = 1 << 6,

        /// <summary>
        /// Force decode HDR content to LDR.
        /// </summary>
        ForceLDR = 1 << 7,

        /// <summary>
        /// Automatically determine codec flags.
        /// </summary>
        Auto = 1 << 30,
    }

    internal class Common
    {
        public static void ApplyTextureConversions(SKBitmap bitmap, TextureCodec decodeFlags)
        {
            var swapRA = decodeFlags.HasFlag(TextureCodec.Dxt5nm);
            var decodeYCoCg = decodeFlags.HasFlag(TextureCodec.YCoCg);
            var decodeHemiOct = decodeFlags.HasFlag(TextureCodec.HemiOctRB);
            var reconstructZ = decodeFlags.HasFlag(TextureCodec.NormalizeNormals);

            if (!swapRA && !decodeYCoCg && !decodeHemiOct && !reconstructZ)
            {
                return;
            }

            var data = bitmap.GetPixelSpan();

            if (swapRA)
            {
                SwapRA(data);
            }

            var pixels = MemoryMarshal.Cast<byte, Color>(data);

            for (var i = 0; i < pixels.Length; i++)
            {
                if (decodeYCoCg)
                {
                    Decode_YCoCg(ref pixels[i]);
                }

                if (decodeHemiOct)
                {
                    Decode_HemiOct(ref pixels[i]);
                }

                if (reconstructZ)
                {
                    ReconstructNormals(ref pixels[i]);
                }
            }
        }

        public static void Decode_YCoCg(ref Color color)
        {
            var scale = (color.b >> 3) + 1;
            var co = (color.r - 128) / scale;
            var cg = (color.g - 128) / scale;

            var y = color.a;

            color.r = ClampColor(y + co - cg);
            color.g = ClampColor(y + cg);
            color.b = ClampColor(y - co - cg);
            color.a = 255;
        }

        public static void ReconstructNormals(ref Color color)
        {
            var swizzleR = (color.r * 2) - 255;     // premul R
            var swizzleG = (color.g * 2) - 255;     // premul G
            var deriveB = (int)MathF.Sqrt((255 * 255) - (swizzleR * swizzleR) - (swizzleG * swizzleG));
            color.r = ClampColor((swizzleR / 2) + 128); // unpremul R and normalize (128 = forward, or facing viewer)
            color.g = ClampColor((swizzleG / 2) + 128); // unpremul G and normalize
            color.b = ClampColor((deriveB / 2) + 128);  // unpremul B and normalize
        }

        public static void Decode_HemiOct(ref Color color)
        {
            var nx = ((color.r + color.g) / 255.0f) - 1.003922f;
            var ny = (color.r - color.g) / 255.0f;
            var nz = 1 - MathF.Abs(nx) - MathF.Abs(ny);

            var l = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
            color.a = color.b; //b to alpha
            color.r = (byte)(((nx / l * 0.5f) + 0.5f) * 255);
            color.g = (byte)(((ny / l * 0.5f) + 0.5f) * 255);
            color.b = (byte)(((nz / l * 0.5f) + 0.5f) * 255);
        }

        public static byte ClampColor(int a)
        {
            if (a > 255)
            {
                return 255;
            }

            return a < 0 ? (byte)0 : (byte)a;
        }

        public static float ClampHighRangeColor(float a)
        {
            if (a > 1f)
            {
                return 1;
            }

            return a < 0f ? 0f : a;
        }

        public static byte ToClampedLdrColor(float a)
        {
            return (byte)(ClampHighRangeColor(a) * 255 + 0.5f);
        }


        private static byte[] CreateSimdSwizzleMask(int[] swizzle) =>
        [
            ..Enumerable.Range(0, 64).Select(component =>
             {
                 var pixel = component / 4;
                 var pixelComponent = component % 4;
                 return (byte)(pixel * 4 + swizzle[pixelComponent]);
             })
        ];

        private static readonly byte[] SwapRBSwizzleMask_RGBA = CreateSimdSwizzleMask([2, 1, 0, 3]);
        private static readonly byte[] SwapRASwizzleMask_BGRA = CreateSimdSwizzleMask([0, 1, 3, 2]);

        /// <remarks>
        /// Components are expected in RGBA order. This changes it to BGRA.
        /// </remarks>
        public static void SwapRB(Span<byte> pixels)
        {
            var offset = SwizzleSimd(pixels, SwapRBSwizzleMask_RGBA);

            // Process remaining pixels with scalar code
            var pixelsInt = MemoryMarshal.Cast<byte, uint>(pixels[offset..]);
            for (var j = 0; j < pixelsInt.Length; j++)
            {
                pixelsInt[j] = BitOperations.RotateRight(BinaryPrimitives.ReverseEndianness(pixelsInt[j]), 8);
            }
        }

        /// <remarks>
        /// Components are expected in BGRA order.
        /// </remarks>
        public static void SwapRA(Span<byte> pixels)
        {
            var offset = SwizzleSimd(pixels, SwapRASwizzleMask_BGRA);

            // Process remaining pixels with scalar code
            var pixelsInt = MemoryMarshal.Cast<byte, uint>(pixels[offset..]);
            for (var j = 0; j < pixelsInt.Length; j++)
            {
                var p = pixelsInt[j];

                var bg = p & 0x0000FFFF;
                var r = (p & 0x00FF0000) << 8;
                var a = (p & 0xFF000000) >> 8;

                pixelsInt[j] = bg | r | a;
            }
        }

        private static int SwizzleSimd(Span<byte> pixels, byte[] swizzleMask)
        {
            var offset = 0;

            // Process 64-byte chunks
            if (Vector512.IsHardwareAccelerated && pixels.Length >= Vector512<byte>.Count)
            {
                var shuffleMask = Vector512.Create(swizzleMask);

                for (; offset <= pixels.Length - Vector512<byte>.Count; offset += Vector512<byte>.Count)
                {
                    var rgba = Vector512.Create<byte>(pixels.Slice(offset, Vector512<byte>.Count));
                    var bgra = Vector512.Shuffle(rgba, shuffleMask);
                    bgra.CopyTo(pixels.Slice(offset, Vector512<byte>.Count));
                }
            }

            // Process 32-byte chunks
            if (Vector256.IsHardwareAccelerated && pixels.Length - offset >= Vector256<byte>.Count)
            {
                var shuffleMask = Vector256.Create(swizzleMask);

                for (; offset <= pixels.Length - Vector256<byte>.Count; offset += Vector256<byte>.Count)
                {
                    var rgba = Vector256.Create<byte>(pixels.Slice(offset, Vector256<byte>.Count));
                    var bgra = Vector256.Shuffle(rgba, shuffleMask);
                    bgra.CopyTo(pixels.Slice(offset, Vector256<byte>.Count));
                }
            }

            // Process 16-byte chunks
            if (Vector128.IsHardwareAccelerated && pixels.Length - offset >= Vector128<byte>.Count)
            {
                var shuffleMask = Vector128.Create(swizzleMask);

                for (; offset <= pixels.Length - Vector128<byte>.Count; offset += Vector128<byte>.Count)
                {
                    var rgba = Vector128.Create<byte>(pixels.Slice(offset, Vector128<byte>.Count));
                    var bgra = Vector128.Shuffle(rgba, shuffleMask);
                    bgra.CopyTo(pixels.Slice(offset, Vector128<byte>.Count));
                }
            }

            return offset;
        }
    }
}
