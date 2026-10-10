namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets the particle color to a random value interpolated between two configurable colors,
    /// optionally tinted by the light arriving at a control point.
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
        private readonly float updateThreshold = 32f;
        private readonly float lightAmplification = 1f;
        private readonly ParticleColorBlendMode tintBlendMode = ParticleColorBlendMode.PARTICLEBLEND_DEFAULT;
        private readonly ParticleField fieldOutput = ParticleField.Color;

        // Light last sampled at the tint control point, and where that point was. Starting infinitely
        // far away forces the first sample.
        private Vector3 sampledPosition = new(float.PositiveInfinity);
        private Vector3 sampledLight = Vector3.One;

        public RandomColor(ParticleDefinitionParser parse) : base(parse)
        {
            colorMin = parse.Color24("m_ColorMin", colorMin);
            colorMax = parse.Color24("m_ColorMax", colorMax);
            tintMin = parse.Color24("m_TintMin", tintMin);
            tintMax = parse.Color24("m_TintMax", tintMax);
            tintPercentage = parse.Float("m_flTintPerc", tintPercentage);
            tintControlPoint = parse.Int32("m_nTintCP", tintControlPoint);
            updateThreshold = parse.Float("m_flUpdateThreshold", updateThreshold);
            lightAmplification = parse.Float("m_flLightAmplification", lightAmplification);
            tintBlendMode = parse.EnumNormalized("m_nTintBlendMode", tintBlendMode);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var color = particleSystemState.Random.NextBetween(colorMin, colorMax);

            if (tintPercentage > 0f)
            {
                var tint = Vector3.Clamp(SampleLight(particleSystemState) * lightAmplification, tintMin, tintMax);
                var tinted = tintBlendMode.Blend(color, tint);

                color = Vector3.Lerp(color, tinted, MathUtils.Saturate(tintPercentage));
            }

            particle.SetVector(fieldOutput, color);

            return particle;
        }

        // Re-sampled only once the control point moves past m_flUpdateThreshold from the last sample.
        private Vector3 SampleLight(ParticleSystemState particleSystemState)
        {
            var position = particleSystemState.GetControlPoint(tintControlPoint).Position;

            if (Vector3.DistanceSquared(position, sampledPosition) > updateThreshold * updateThreshold)
            {
                sampledLight = particleSystemState.Lighting.SampleAmbientLight(position);
                sampledPosition = position;
            }

            return sampledLight;
        }
    }
}
