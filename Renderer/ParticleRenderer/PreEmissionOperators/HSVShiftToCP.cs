namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Compares a colour carried on one control point against the effect's default colour and writes the
    /// difference to another control point as an HSV shift: normalized hue offset in X, saturation ratio
    /// minus one in Y, value ratio minus one in Z. Sprite shaders read that triple to tint a shared
    /// texture without a per-colour material.
    ///
    /// <para>The colour control point holds its RGB in 0-255. The shift is only produced while the
    /// enable control point's X component is positive; otherwise the output control point is zeroed.
    /// A default colour with no saturation or no value zeroes the hue offset as well.</para>
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_HSVShiftToCP">C_OP_HSVShiftToCP</seealso>
    class HSVShiftToCP : ParticleFunctionPreEmissionOperator
    {
        private readonly int colorCP = 60;
        private readonly int colorGemEnableCP = 61;
        private readonly int outputCP = 62;
        private readonly Vector3 defaultColor = Vector3.One;

        private readonly Vector3 defaultHSV;

        public HSVShiftToCP(ParticleDefinitionParser parse) : base(parse)
        {
            colorCP = parse.Int32("m_nColorCP", colorCP);
            colorGemEnableCP = parse.Int32("m_nColorGemEnableCP", colorGemEnableCP);
            outputCP = parse.Int32("m_nOutputCP", outputCP);
            defaultColor = parse.Color24("m_DefaultHSVColor", defaultColor);

            defaultHSV = RgbToHsv(defaultColor);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            if (particleSystemState.GetControlPoint(colorGemEnableCP).Position.X <= 0f)
            {
                particleSystemState.SetControlPointValue(outputCP, Vector3.Zero);
                return;
            }

            var hsv = RgbToHsv(particleSystemState.GetControlPoint(colorCP).Position / 255f);

            var hueDelta = (hsv.X - defaultHSV.X) / 360f;

            if (hueDelta < 0f)
            {
                hueDelta += 1f;
            }

            var gate = 1f;

            var saturationRatio = 1f;
            if (defaultHSV.Y > 0f)
            {
                saturationRatio = hsv.Y / defaultHSV.Y;
            }
            else
            {
                gate = 0f;
            }

            var valueRatio = 1f;
            if (defaultHSV.Z > 0f)
            {
                valueRatio = hsv.Z / defaultHSV.Z;
            }
            else
            {
                gate = 0f;
            }

            particleSystemState.SetControlPointValue(outputCP,
                new Vector3(gate * hueDelta, saturationRatio - 1f, valueRatio - 1f));
        }

        /// <summary>
        /// Converts a normalized RGB triplet to (hue in degrees, saturation, value).
        /// </summary>
        private static Vector3 RgbToHsv(Vector3 rgb)
        {
            var max = MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z));
            var min = MathF.Min(rgb.X, MathF.Min(rgb.Y, rgb.Z));
            var chroma = max - min;

            var value = max;
            var saturation = max == 0f ? 0f : chroma / max;

            if (saturation == 0f)
            {
                return new Vector3(0f, saturation, value);
            }

            float sextant;

            if (rgb.X == max)
            {
                sextant = (rgb.Y - rgb.Z) / chroma;
            }
            else if (rgb.Y == max)
            {
                sextant = ((rgb.Z - rgb.X) / chroma) + 2f;
            }
            else
            {
                sextant = ((rgb.X - rgb.Y) / chroma) + 4f;
            }

            var hue = sextant * 60f;

            if (hue < 0f)
            {
                hue += 360f;
            }

            return new Vector3(hue, saturation, value);
        }
    }
}
