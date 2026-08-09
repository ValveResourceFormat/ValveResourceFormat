namespace ValveResourceFormat.Renderer.Particles.Utils
{
    /// <summary>
    /// Tolerances for treating a near-zero quantity as zero. These guard normalisation and division,
    /// they are not thresholds the engine authors: a value the engine compares exactly should be
    /// compared exactly here too.
    /// </summary>
    static class Epsilon
    {
        /// <summary>Length below which a vector counts as having no direction.</summary>
        public const float Length = 1e-6f;

        /// <summary>Squared length below which a vector is too short to normalise.</summary>
        public const float LengthSquared = 1e-12f;

        /// <summary>Added to a duration the engine divides by, so a zero-length ramp is a step.</summary>
        public const float Duration = 1.1920929e-7f;
    }
}
