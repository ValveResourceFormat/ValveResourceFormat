namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets the particle color to a random value interpolated between two configurable colors,
    /// optionally blended toward a tint carried on a control point.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomColor">C_INIT_RandomColor</seealso>
    class RandomColor : ParticleFunctionInitializer
    {
        private readonly Vector3 colorMin = Vector3.One;
        private readonly Vector3 colorMax = Vector3.One;
        private readonly Vector3 tintMin = Vector3.Zero;
        private readonly Vector3 tintMax = Vector3.One;
        private readonly float tintPercentage;
        private readonly int tintControlPoint;
        private readonly ParticleColorBlendType tintBlendMode = ParticleColorBlendType.PARTICLE_COLOR_BLEND_MULTIPLY;
        private readonly ParticleField fieldOutput = ParticleField.Color;

        public RandomColor(ParticleDefinitionParser parse) : base(parse)
        {
            colorMin = parse.Color24("m_ColorMin", colorMin);
            colorMax = parse.Color24("m_ColorMax", colorMax);
            tintMin = parse.Color24("m_TintMin", tintMin);
            tintMax = parse.Color24("m_TintMax", tintMax);
            tintPercentage = parse.Float("m_flTintPerc", tintPercentage);
            tintControlPoint = parse.Int32("m_nTintCP", tintControlPoint);
            tintBlendMode = parse.EnumNormalized<ParticleColorBlendType>("m_nTintBlendMode", tintBlendMode);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var color = particleSystemState.Random.NextBetween(colorMin, colorMax);

            // Tinting is off unless the effect asks for it; the tint colour itself is driven onto a
            // control point game-side and clamped here to the authored range.
            if (tintPercentage > 0f)
            {
                var tint = Vector3.Clamp(particleSystemState.GetControlPoint(tintControlPoint).Position, tintMin, tintMax);

                var tinted = tintBlendMode == ParticleColorBlendType.PARTICLE_COLOR_BLEND_ADD
                    ? color + tint
                    : color * tint;

                color = Vector3.Lerp(color, tinted, MathUtils.Saturate(tintPercentage));
            }

            particle.SetVector(fieldOutput, color);

            return particle;
        }
    }
}
