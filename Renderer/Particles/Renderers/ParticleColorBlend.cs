using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Combines a particle's colour with a renderer's colour scale the way <c>m_nColorBlendType</c>
    /// asks. Colours are in byte units, 0 to 255, and the result is saturated to that range.
    /// </summary>
    internal static class ParticleColorBlend
    {
        private static readonly Vector3 LuminanceWeights = new(0.2125f, 0.7154f, 0.0721f);
        private static readonly Vector3 White = new(255f);

        /// <summary>
        /// Brings an authored colour scale into the units <see cref="Apply"/> combines it in: doubled
        /// for the 2x modes, in bytes for the modes that add or replace, and inverted for screen.
        /// </summary>
        public static Vector3 ConvertScale(ParticleColorBlendType type, Vector3 scale) => type switch
        {
            ParticleColorBlendType.PARTICLE_COLOR_BLEND_MULTIPLY2X or ParticleColorBlendType.PARTICLE_COLOR_BLEND_MOD2X => scale * 2f,
            ParticleColorBlendType.PARTICLE_COLOR_BLEND_ADD
                or ParticleColorBlendType.PARTICLE_COLOR_BLEND_SUBTRACT
                or ParticleColorBlendType.PARTICLE_COLOR_BLEND_MAX
                or ParticleColorBlendType.PARTICLE_COLOR_BLEND_MIN
                or ParticleColorBlendType.PARTICLE_COLOR_BLEND_REPLACE
                or ParticleColorBlendType.PARTICLE_COLOR_BLEND_AVERAGE => scale * 255f,
            ParticleColorBlendType.PARTICLE_COLOR_BLEND_SCREEN => Vector3.One - scale,
            _ => scale,
        };

        /// <summary>Combines a byte colour with a scale from <see cref="ConvertScale"/>.</summary>
        /// <param name="type">How the colour channels combine.</param>
        /// <param name="color">The particle's colour and alpha, in bytes.</param>
        /// <param name="scale">The converted colour scale, and the alpha scale in w, which always multiplies.</param>
        public static Vector4 Apply(ParticleColorBlendType type, Vector4 color, Vector4 scale)
        {
            var c = color.AsVector3();
            var s = scale.AsVector3();

            var rgb = type switch
            {
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_DIVIDE => c / s,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_ADD => c + s,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_SUBTRACT => c - s,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_SCREEN => White - ((White - c) * s),
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_MAX => Vector3.Max(c, s),
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_MIN => Vector3.Min(c, s),
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_REPLACE => s,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_AVERAGE => (c + s) * 0.5f,
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_NEGATE => White - (c * s),
                ParticleColorBlendType.PARTICLE_COLOR_BLEND_LUMINANCE => Vector3.Dot(s, LuminanceWeights) * c,
                _ => c * s,
            };

            return Vector4.Clamp(new Vector4(rgb, color.W * scale.W), Vector4.Zero, new Vector4(255f));
        }

        /// <summary>A colour in 0 to 1 as saturated bytes.</summary>
        public static Vector4 ToBytes(Vector3 color, float alpha)
            => Vector4.Clamp(new Vector4(color, alpha), Vector4.Zero, Vector4.One) * 255f;

        /// <summary>
        /// A colour in 0 to 1 as bytes truncated without a clamp, so a channel past 1 wraps around, with
        /// <paramref name="alpha"/> as given.
        /// </summary>
        public static Vector4 ToWrappedBytes(Vector3 color, float alpha)
        {
            var bytes = color * 255f;

            return new Vector4((byte)(int)bytes.X, (byte)(int)bytes.Y, (byte)(int)bytes.Z, alpha);
        }
    }
}
