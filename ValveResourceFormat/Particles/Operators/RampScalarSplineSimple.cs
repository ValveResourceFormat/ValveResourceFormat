using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Ramps a scalar particle attribute within a normalized-age window by a rate scaled by the
    /// smoothstep of the particle's place in the window, optionally rising and falling again.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RampScalarSplineSimple">C_OP_RampScalarSplineSimple</seealso>
    class RampScalarSplineSimple : ParticleFunctionOperator
    {
        private readonly float rate;
        private readonly float startTime;
        private readonly float endTime = 1f;
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly bool easeOut;

        public RampScalarSplineSimple(ParticleDefinitionParser parse) : base(parse)
        {
            rate = parse.Float("m_Rate", rate);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            outputField = parse.ParticleField("m_nField", outputField);
            easeOut = parse.Boolean("m_bEaseOut", easeOut);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var step = strength * frameTime * rate;
            var inverseWindow = 1f / (endTime - startTime + ParticleMath.FloatEpsilon);
            var (min, max) = outputField.SetFloatRange();

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var ages = current.ReadAges();

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var output = current.Floats(outputField);

            for (var i = 0; i < output.Length; i++)
            {
                if (lifetimes[i] <= 0f)
                {
                    continue;
                }

                var normalizedAge = ages[i] / lifetimes[i];

                if (normalizedAge < startTime || normalizedAge >= endTime)
                {
                    continue;
                }

                var fraction = MathUtils.Saturate((normalizedAge - startTime) * inverseWindow);

                if (easeOut)
                {
                    fraction = (fraction > 0.5f ? 1f - fraction : fraction) * 2f;
                }

                output[i] = Math.Clamp(output[i] + (MathUtils.Smoothstep(0f, 1f, fraction) * step), min, max);
            }
        }
    }
}
