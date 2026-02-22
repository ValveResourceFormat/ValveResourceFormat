namespace ValveResourceFormat.Serialization.VfxEval
{
    /// <summary>
    /// Implementations for built in VFXEval functions
    /// </summary>
    public static class VfxEvalFunctions
    {
        // Rec. 709 luminance coefficients, normalized to unit length for axis-angle rotation
        private static readonly Vector3 LuminanceCoefficientsNormalised = Vector3.Normalize(new Vector3(0.2126f, 0.7152f, 0.0722f));

        private static float RgbSaturation(Vector3 rgb)
        {
            var max = MathF.Max(MathF.Max(rgb.X, rgb.Y), rgb.Z);
            var min = MathF.Min(MathF.Min(rgb.X, rgb.Y), rgb.Z);
            return max == 0f ? 0f : (max - min) / max;
        }

        /// <summary>
        /// Builds a 4x4 color correction matrix that applies contrast, saturation, and brightness adjustments.
        /// </summary>
        /// <param name="CSB">Color correction parameters: X = contrast, Y = saturation, Z = brightness.</param>
        /// <param name="colorOffset">Pivot point for the brightness gain.</param>
        public static Matrix4x4 MatrixColorCorrect2(Vector3 CSB, Vector3 colorOffset)
        {
            var cross = Vector3.Cross(LuminanceCoefficientsNormalised, Vector3.UnitZ);
            var angle = MathF.Atan2(cross.Length(), Vector3.Dot(LuminanceCoefficientsNormalised, Vector3.UnitZ));
            var rotation = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(cross), angle);

            // T(-offset) × Scale(contrast) × T(offset) × Scale(brightness) × Scale(lum) × R × Scale(sat,sat,1) × R⁻¹ × Scale(1/lum)
            var result = Matrix4x4.CreateTranslation(-colorOffset) * Matrix4x4.CreateScale(CSB.X) * Matrix4x4.CreateTranslation(colorOffset);
            result *= Matrix4x4.CreateScale(CSB.Z);
            result *= Matrix4x4.CreateScale(LuminanceCoefficientsNormalised);
            result *= rotation;
            result *= Matrix4x4.CreateScale(CSB.Y, CSB.Y, 1f);
            result *= Matrix4x4.Transpose(rotation);
            result *= Matrix4x4.CreateScale(Vector3.One / LuminanceCoefficientsNormalised);

            return Matrix4x4.Transpose(result);
        }

        /// <summary>
        /// Builds a luminance-preserving saturation adjustment matrix in RGB space.
        /// </summary>
        /// <param name="rgb">Reference color used to derive hue and saturation.</param>
        /// <param name="strength">Saturation adjustment strength.</param>
        public static Matrix4x4 MatrixColorTint2(Vector3 rgb, float strength)
        {
            var saturation = RgbSaturation(rgb);

            var cross = Vector3.Cross(LuminanceCoefficientsNormalised, Vector3.UnitZ);
            var angle = MathF.Atan2(cross.Length(), Vector3.Dot(LuminanceCoefficientsNormalised, Vector3.UnitZ));
            var rotation = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(cross), angle);
            var lumScale = Matrix4x4.CreateScale(LuminanceCoefficientsNormalised);
            var gray = Vector3.Transform(rgb, lumScale * rotation);

            var desatXY = 1f - saturation;
            var satFactor = 1f - saturation * saturation * strength;

            // Scale(lum) × R × T(-gray) × Scale(desatXY,desatXY,satFactor) × T(gray') × R⁻¹ × Scale(1/lum)
            var result = lumScale * rotation;
            result *= Matrix4x4.CreateTranslation(-gray.X, -gray.Y, 0f);
            result *= Matrix4x4.CreateScale(desatXY, desatXY, satFactor);
            result *= Matrix4x4.CreateTranslation(gray.X, gray.Y, (1f - satFactor) * gray.Z);
            result *= Matrix4x4.Transpose(rotation);
            result *= Matrix4x4.CreateScale(Vector3.One / LuminanceCoefficientsNormalised);

            return Matrix4x4.Transpose(result);
        }
    }
}
