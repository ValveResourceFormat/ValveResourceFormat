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
        /// Converts a sRGB gamma space to linear RGB color.
        /// </summary>
        public static Vector3 SrgbGammaToLinear(Vector3 vSrgbGammaColor)
        {
            var vLinearSegment = vSrgbGammaColor / 12.92f;
            const float power = 2.4f;

            var vExpSegment = (vSrgbGammaColor / 1.055f) + new Vector3(0.055f / 1.055f);
            vExpSegment = new Vector3(
                MathF.Pow(vExpSegment.X, power),
                MathF.Pow(vExpSegment.Y, power),
                MathF.Pow(vExpSegment.Z, power)
            );

            var vLinearColor = new Vector3(
                (vSrgbGammaColor.X <= 0.04045f) ? vLinearSegment.X : vExpSegment.X,
                (vSrgbGammaColor.Y <= 0.04045f) ? vLinearSegment.Y : vExpSegment.Y,
                (vSrgbGammaColor.Z <= 0.04045f) ? vLinearSegment.Z : vExpSegment.Z
            );

            return vLinearColor;
        }
    }
}
