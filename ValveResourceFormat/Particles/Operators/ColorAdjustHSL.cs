using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Shifts the hue, saturation and lightness of each particle's initial colour and writes the
    /// result as its colour every step. Because it starts from the initial colour, colour changes made
    /// by earlier operators are replaced.
    /// </summary>
    /// <remarks>
    /// The hue shift is in degrees and wraps; saturation and lightness are added and clamped to [0, 1].
    /// Operator strength is not read.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ColorAdjustHSL">C_OP_ColorAdjustHSL</seealso>
    class ColorAdjustHSL : ParticleFunctionOperator
    {
        private readonly INumberProvider hueAdjust = new LiteralNumberProvider(0f);
        private readonly INumberProvider saturationAdjust = new LiteralNumberProvider(0f);
        private readonly INumberProvider lightnessAdjust = new LiteralNumberProvider(0f);

        public ColorAdjustHSL(ParticleDefinitionParser parse) : base(parse)
        {
            hueAdjust = parse.NumberProvider("m_flHueAdjust", hueAdjust);
            saturationAdjust = parse.NumberProvider("m_flSaturationAdjust", saturationAdjust);
            lightnessAdjust = parse.NumberProvider("m_flLightnessAdjust", lightnessAdjust);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var (hue, saturation, lightness) = RgbToHsl(particle.GetInitialVector(particles, ParticleField.Color));

                hue = (hue + hueAdjust.NextNumber(ref particle, particleSystemState)) % 360f;

                if (hue < 0f)
                {
                    hue += 360f;
                }

                saturation = ParticleMath.ClampToUnit(saturation + saturationAdjust.NextNumber(ref particle, particleSystemState));
                lightness = ParticleMath.ClampToUnit(lightness + lightnessAdjust.NextNumber(ref particle, particleSystemState));

                particle.Color = HslToRgb(hue, saturation, lightness);
            }
        }

        /// <summary>Converts a normalized RGB colour to hue in degrees, saturation and lightness.</summary>
        private static (float Hue, float Saturation, float Lightness) RgbToHsl(Vector3 rgb)
        {
            var min = rgb.MinComponent();
            var max = rgb.MaxComponent();
            var sum = min + max;
            var lightness = sum * 0.5f;
            var delta = max - min;

            if (MathF.Abs(delta) < 1e-4f)
            {
                return (0f, 0f, lightness);
            }

            var saturation = delta / (lightness >= 0.5f ? 2f - sum : sum);

            if (saturation == 0f)
            {
                return (0f, 0f, lightness);
            }

            float hue;

            if (rgb.X == max)
            {
                hue = (rgb.Y - rgb.Z) / delta;
            }
            else if (rgb.Y == max)
            {
                hue = ((rgb.Z - rgb.X) / delta) + 2f;
            }
            else
            {
                hue = ((rgb.X - rgb.Y) / delta) + 4f;
            }

            hue *= 60f;

            if (hue < 0f)
            {
                hue += 360f;
            }

            return (hue, saturation, lightness);
        }

        /// <summary>Converts hue in degrees, saturation and lightness to a normalized RGB colour.</summary>
        private static Vector3 HslToRgb(float hue, float saturation, float lightness)
        {
            if (saturation == 0f)
            {
                return new Vector3(lightness);
            }

            var sector = hue % 360f / 60f;
            var chroma = (1f - MathF.Abs(lightness + lightness - 1f)) * saturation;
            var second = (1f - MathF.Abs((sector % 2f) - 1f)) * chroma;

            var rgb = (int)sector switch
            {
                0 => new Vector3(chroma, second, 0f),
                1 => new Vector3(second, chroma, 0f),
                2 => new Vector3(0f, chroma, second),
                3 => new Vector3(0f, second, chroma),
                4 => new Vector3(second, 0f, chroma),
                _ => new Vector3(chroma, 0f, second),
            };

            return rgb + new Vector3(lightness - (chroma * 0.5f));
        }
    }
}
