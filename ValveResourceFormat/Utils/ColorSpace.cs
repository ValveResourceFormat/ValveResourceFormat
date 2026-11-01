namespace ValveResourceFormat.Utils
{
    /// <summary>
    /// Color space conversion utilities.
    /// </summary>
    public static class ColorSpace
    {
        /// <summary>
        /// Converts a linear RGB color to sRGB gamma space.
        /// </summary>
        public static Vector3 SrgbLinearToGamma(Vector3 vLinearColor)
        {
            var vLinearSegment = vLinearColor * 12.92f;
            const float power = 1.0f / 2.4f;

            var vExpSegment = new Vector3(
                MathF.Pow(vLinearColor.X, power),
                MathF.Pow(vLinearColor.Y, power),
                MathF.Pow(vLinearColor.Z, power)
            );

            vExpSegment *= 1.055f;
            vExpSegment -= new Vector3(0.055f);

            var vGammaColor = new Vector3(
                (vLinearColor.X <= 0.0031308f) ? vLinearSegment.X : vExpSegment.X,
                (vLinearColor.Y <= 0.0031308f) ? vLinearSegment.Y : vExpSegment.Y,
                (vLinearColor.Z <= 0.0031308f) ? vLinearSegment.Z : vExpSegment.Z
            );

            return vGammaColor;
        }

        /// <summary>
        /// Converts a linear RGB color to gamma space with a plain 2.2 exponent, the curve the map compiler
        /// bakes draw call tints with.
        /// </summary>
        public static Vector3 LinearToGamma22(Vector3 vLinearColor)
        {
            const float power = 1.0f / 2.2f;

            return new Vector3(
                MathF.Pow(vLinearColor.X, power),
                MathF.Pow(vLinearColor.Y, power),
                MathF.Pow(vLinearColor.Z, power)
            );
        }

        /// <summary>
        /// Converts an sRGB gamma space color to linear RGB.
        /// </summary>
        public static Vector3 SrgbGammaToLinear(Vector3 vSrgbGammaColor)
        {
            var vLinearSegment = vSrgbGammaColor / 12.92f;
            const float power = 2.4f;

            var vExpSegment = Vector3.FusedMultiplyAdd(vSrgbGammaColor, new Vector3(1f / 1.055f), new Vector3(0.055f / 1.055f));

            // note: pow(x, y) = exp(y*log(x)) when x > 0
            vExpSegment = Vector3.Exp(power * Vector3.Log(vExpSegment));
            vExpSegment -= new Vector3(0.0000001f); // Fix error with Vector3.Exp(Vector3.Zero)

            var vLinearColor = new Vector3(
                (vSrgbGammaColor.X <= 0.04045f) ? vLinearSegment.X : vExpSegment.X,
                (vSrgbGammaColor.Y <= 0.04045f) ? vLinearSegment.Y : vExpSegment.Y,
                (vSrgbGammaColor.Z <= 0.04045f) ? vLinearSegment.Z : vExpSegment.Z
            );

            return vLinearColor;
        }

        /// <summary>
        /// Converts a linear RGB color to Oklab, where X is the lightness and Y and Z are the green-red and blue-yellow axes.
        /// </summary>
        public static Vector3 LinearToOklab(Vector3 linearColor)
        {
            var l = MathF.Cbrt(0.4122214708f * linearColor.X + 0.5363325363f * linearColor.Y + 0.0514459929f * linearColor.Z);
            var m = MathF.Cbrt(0.2119034982f * linearColor.X + 0.6806995451f * linearColor.Y + 0.1073969566f * linearColor.Z);
            var s = MathF.Cbrt(0.0883024619f * linearColor.X + 0.2817188376f * linearColor.Y + 0.6299787005f * linearColor.Z);

            return new Vector3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s
            );
        }

        /// <summary>
        /// Converts a color temperature to an sRGB gamma space color, the way light entities with a color temperature do.
        /// </summary>
        /// <param name="kelvin">Color temperature in Kelvin.</param>
        public static Vector3 ColorTemperatureToSrgb(float kelvin)
        {
            var t = kelvin / 100f;

            var red = t > 66f ? 329.69873f * MathF.Pow(t - 60f, -0.13320476f) : 255f;
            var green = t > 66f
                ? 288.12216f * MathF.Pow(t - 60f, -0.075514846f)
                : 99.4708f * MathF.Log(t) - 161.11957f;
            var blue = t < 66f ? 138.51773f * MathF.Log(t - 10f) - 305.0448f : 255f;

            // MaxNumber turns the NaN of a logarithm below its domain into zero
            static float Channel(float value) => float.MinNumber(float.MaxNumber(value, 0f), 255f) / 255f;

            return new Vector3(Channel(red), Channel(green), Channel(blue));
        }
    }
}
