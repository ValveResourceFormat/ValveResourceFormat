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
    }
}
