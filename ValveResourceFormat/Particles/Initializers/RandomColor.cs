using ValveResourceFormat.Particles.Utils;

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
            tintBlendMode = parse.Enum("m_nTintBlendMode", tintBlendMode);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
        }

        public override ulong WrittenFields => fieldOutput.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var colors = ParticleBuffer.IsVector(fieldOutput) ? run.Vectors(fieldOutput) : default;
            var tint = Tint(particleSystemState);

            for (var i = 0; i < run.Length; i++)
            {
                var color = particleSystemState.Random.NextBetween(colorMin, colorMax);
                WriteColor(colors, i, color, tint);
            }
        }

        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var colors = ParticleBuffer.IsVector(fieldOutput) ? run.Vectors(fieldOutput) : default;
            var tint = Tint(particleSystemState);

            // One draw per block of four from the thread generator, a value per particle
            foreach (var (offset, block) in run.Blocks())
            {
                var draws = particleSystemState.ThreadRandom.Next();

                for (var i = 0; i < block.Length; i++)
                {
                    var color = MathUtils.Lerp(colorMin, colorMax, draws[i]);
                    WriteColor(colors, offset + i, color, tint);
                }
            }
        }

        /// <summary>The tint of the light at the tint control point, or zero when nothing is tinted.</summary>
        private Vector3 Tint(ParticleSystemState particleSystemState)
        {
            if (tintPercentage <= 0f)
            {
                return Vector3.Zero;
            }

            return ParticleMath.LightTint(SampleLight(particleSystemState), tintMin, tintMax, lightAmplification);
        }

        /// <summary>Tints <paramref name="color"/> and stores it at <paramref name="index"/> of <paramref name="colors"/>, unless that is empty.</summary>
        private void WriteColor(Span<Vector3> colors, int index, Vector3 color, Vector3 tint)
        {
            if (tintPercentage > 0f)
            {
                var tinted = tintBlendMode.Blend(color, tint);

                color = Vector3.Min(MathUtils.Lerp(color, tinted, tintPercentage), Vector3.One);
            }

            if (!colors.IsEmpty)
            {
                colors[index] = color;
            }
        }

        public override void Reset()
        {
            sampledPosition = Vector3.Zero;
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

            // Only a missing sample is checked for here, not a different control point, so a second tint initializer
            // on another control point would reuse this light until its own control point moved past the threshold
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
