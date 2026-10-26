namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Linearly ramps a scalar particle attribute by a constant rate per second within a
    /// normalized-age window.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RampScalarLinearSimple">C_OP_RampScalarLinearSimple</seealso>
    class RampScalarLinearSimple : ParticleFunctionOperator
    {
        private readonly float rate;
        private readonly float startTime;
        private readonly float endTime = 1.0f;
        private readonly ParticleField outputField = ParticleField.Radius;

        public RampScalarLinearSimple(ParticleDefinitionParser parse) : base(parse)
        {
            rate = parse.Float("m_Rate", rate);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
            outputField = parse.ParticleField("m_nField", outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var step = rate * (strength * frameTime);
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
                var lifetime = lifetimes[i];

                // The start/end window is in normalized age, like FadeInSimple
                var normalizedAge = ages[i] / lifetime;
                var ramping = lifetime > 0f && startTime <= normalizedAge && normalizedAge < endTime;

                if (ramping)
                {
                    output[i] = Math.Clamp(output[i] + step, min, max);
                }
            }
        }
    }
}
