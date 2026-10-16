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

        /// <summary>
        /// Shifts a colour in 0 to 1 by an HSV shift: x turns the hue by that fraction of a turn, y and z
        /// scale the saturation and value, each clamped to 0 to 1. The result is rounded to bytes.
        /// </summary>
        public static Vector3 ShiftHsv(Vector3 rgb, Vector3 shift)
        {
            var max = rgb.MaxComponent();
            var chroma = max - rgb.MinComponent();
            var hue = 0f;

            if (chroma > 0f)
            {
                var sextant = rgb.X == max ? (rgb.Y - rgb.Z) / chroma
                    : rgb.Y == max ? ((rgb.Z - rgb.X) / chroma) + 2f
                    : ((rgb.X - rgb.Y) / chroma) + 4f;

                hue = sextant * 60f;

                if (hue < 0f)
                {
                    hue += 360f;
                }
            }

            hue += shift.X * 360f;

            if (hue > 360f)
            {
                hue -= 360f;
            }
            else if (hue < 0f)
            {
                hue += 360f;
            }

            var saturation = MathUtils.Saturate((max > 0f ? chroma / max : 0f) * shift.Y);
            var value = MathUtils.Saturate(max * shift.Z);

            var c = value * saturation;
            var h = hue / 60f;
            var x = c * (1f - MathF.Abs((h % 2f) - 1f));
            var shifted = (int)h switch
            {
                0 or 6 => new Vector3(c, x, 0f),
                1 => new Vector3(x, c, 0f),
                2 => new Vector3(0f, c, x),
                3 => new Vector3(0f, x, c),
                4 => new Vector3(x, 0f, c),
                _ => new Vector3(c, 0f, x),
            } + new Vector3(value - c);

            return new Vector3(ToByte(shifted.X), ToByte(shifted.Y), ToByte(shifted.Z)) / 255f;

            static float ToByte(float x) => Math.Min((int)((x * 255f) + 0.5f), 255);
        }

        /// <summary>A colour in 0 to 1 as saturated bytes.</summary>
        public static Vector4 ToBytes(Vector3 color, float alpha)
            => Vector4.Clamp(new Vector4(color, alpha), Vector4.Zero, Vector4.One) * 255f;
    }
}
