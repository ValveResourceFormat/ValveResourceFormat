using System;
using System.Runtime.InteropServices;

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

    internal class Common
    {
        public static void Undo_YCoCg(ref Color color)
        {
            var s = (color.b >> 3) + 1;
            var co = (color.r - 128) / s;
            var cg = (color.g - 128) / s;

            var y = color.a;

            color.r = ClampColor(y + co - cg);
            color.g = ClampColor(y + cg);
            color.b = ClampColor(y - co - cg);
            color.a = 255;
        }

        public static void Undo_NormalizeNormals(ref Color color)
        {
            var swizzleR = (color.r * 2) - 255;     // premul R
            var swizzleG = (color.g * 2) - 255;     // premul G
            var deriveB = (int)MathF.Sqrt((255 * 255) - (swizzleR * swizzleR) - (swizzleG * swizzleG));
            color.r = ClampColor((swizzleR / 2) + 128); // unpremul R and normalize (128 = forward, or facing viewer)
            color.g = ClampColor((swizzleG / 2) + 128); // unpremul G and normalize
            color.b = ClampColor((deriveB / 2) + 128);  // unpremul B and normalize
        }

        public static void Undo_HemiOct(ref Color color)
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
    }
}
