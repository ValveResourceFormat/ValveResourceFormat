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

        private Vector3 sampledPosition;

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
                var tint = Vector3.Clamp(SampleLight(particleSystemState), tintMin, tintMax) * lightAmplification;
                var tinted = tintBlendMode.Blend(color, tint);

                color = Vector3.Min(((tinted - color) * tintPercentage) + color, Vector3.One);
            }

            particle.SetVector(fieldOutput, color);

            return particle;
        }

        public override void Reset()
        {
            sampledPosition = Vector3.Zero;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        /// <summary>
        /// The light at the tint control point. A parent system's sample at the same control point is
        /// reused; otherwise the system's own sample holds until the control point moves
        /// <c>m_flUpdateThreshold</c> away from where this initializer last sampled it.
        /// </summary>
        private Vector3 SampleLight(ParticleSystemState particleSystemState)
        {
            if (particleSystemState.ParentSystem is { } parent && parent.TintCacheControlPoint == tintControlPoint)
            {
                return parent.TintCacheColor;
            }

            var position = particleSystemState.GetControlPoint(tintControlPoint).Position;

            if (particleSystemState.TintCacheControlPoint != tintControlPoint
                || Vector3.DistanceSquared(position, sampledPosition) >= updateThreshold * updateThreshold)
            {
                particleSystemState.TintCacheColor = particleSystemState.Lighting.SampleAmbientLight(position);
                particleSystemState.TintCacheControlPoint = tintControlPoint;
                sampledPosition = position;
            }

            return particleSystemState.TintCacheColor;
        }
    }
}
