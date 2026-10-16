namespace ValveResourceFormat.Particles
{
    static class ParticleColorBlendModeExtensions
    {
        // Combines a colour with a tint the way a ParticleColorBlendMode_t field asks. DEFAULT replaces.
        public static Vector3 Blend(this ParticleColorBlendMode mode, Vector3 color, Vector3 tint) => mode switch
        {
            ParticleColorBlendMode.PARTICLEBLEND_OVERLAY => new Vector3(Overlay(color.X, tint.X), Overlay(color.Y, tint.Y), Overlay(color.Z, tint.Z)),
            ParticleColorBlendMode.PARTICLEBLEND_DARKEN => Vector3.Min(color, tint),
            ParticleColorBlendMode.PARTICLEBLEND_LIGHTEN => Vector3.Max(color, tint),
            ParticleColorBlendMode.PARTICLEBLEND_MULTIPLY => color * tint,
            _ => tint,
        };

        private static float Overlay(float color, float tint)
            => color < 0.5f ? 2f * color * tint : 1f - (2f * (1f - color) * (1f - tint));
    }
}
