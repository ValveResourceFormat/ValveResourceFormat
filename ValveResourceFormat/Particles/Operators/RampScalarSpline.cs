using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Ramps a scalar particle attribute by a per-particle random rate within a per-particle random
    /// time window, the rate eased in across the window by a bias curve, optionally rising and falling
    /// again, and clamped to the output field's valid range.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RampScalarSpline">C_OP_RampScalarSpline</seealso>
    class RampScalarSpline : ParticleFunctionOperator
    {
        /// <summary>Table offsets separating this operator's rate, start time and end time draws.</summary>
        private const int RateOffset = 0;
        private const int StartTimeOffset = 11;
        private const int EndTimeOffset = 12;

        private readonly float rateMin;
        private readonly float rateMax;
        private readonly float startTimeMin;
        private readonly float startTimeMax;
        private readonly float endTimeMin = 1f;
        private readonly float endTimeMax = 1f;
        private readonly float bias = 0.5f;
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly bool proportional = true;
        private readonly bool easeOut;

        public RampScalarSpline(ParticleDefinitionParser parse) : base(parse)
        {
            rateMin = parse.Float("m_RateMin", rateMin);
            rateMax = parse.Float("m_RateMax", rateMax);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
            bias = parse.Float("m_flBias", bias);
            outputField = parse.ParticleField("m_nField", outputField);
            proportional = parse.Boolean("m_bProportionalOp", proportional);
            easeOut = parse.Boolean("m_bEaseOut", easeOut);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (bias <= 0f)
            {
                return;
            }

            var step = strength * frameTime;
            var (min, max) = outputField.SetFloatRange();

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var ages = current.ReadAges();

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var ids = current.ReadIntegers(ParticleField.ParticleId);
            var output = current.Floats(outputField);

            for (var i = 0; i < output.Length; i++)
            {
                if (lifetimes[i] <= 0f)
                {
                    continue;
                }

                var time = proportional ? ages[i] / lifetimes[i] : ages[i];
                var startTime = particleSystemState.Random.ForParticleBetween(ids[i], StartTimeOffset, startTimeMin, startTimeMax);
                var endTime = particleSystemState.Random.ForParticleBetween(ids[i], EndTimeOffset, endTimeMin, endTimeMax);

                if (time < startTime || time >= endTime)
                {
                    continue;
                }

                var rate = particleSystemState.Random.ForParticleBetween(ids[i], RateOffset, rateMin, rateMax);
                var fraction = MathUtils.Saturate((time - startTime) / (endTime - startTime + ParticleMath.FloatEpsilon));

                if (easeOut)
                {
                    fraction = (fraction > 0.5f ? 1f - fraction : fraction) * 2f;
                }

                var value = output[i] + (ParticleMath.Bias(fraction, bias) * step * rate);
                output[i] = Math.Clamp(value, min, max);
            }
        }
    }
}
