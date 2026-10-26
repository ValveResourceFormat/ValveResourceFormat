using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Linearly ramps a scalar particle attribute by a per-particle random rate within a per-particle
    /// random time window, clamped to the output field's valid range.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RampScalarLinear">C_OP_RampScalarLinear</seealso>
    class RampScalarLinear : ParticleFunctionOperator
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
        private readonly bool proportional = true;
        private readonly ParticleField outputField = ParticleField.Radius;

        private readonly bool hasTimeWindow;
        private readonly float clampMin;
        private readonly float clampMax;

        public RampScalarLinear(ParticleDefinitionParser parse) : base(parse)
        {
            rateMin = parse.Float("m_RateMin", rateMin);
            rateMax = parse.Float("m_RateMax", rateMax);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
            proportional = parse.Boolean("m_bProportionalOp", proportional);
            outputField = parse.ParticleField("m_nField", outputField);

            // A window spanning the whole normalized lifetime is not tested at all, so particles
            // past their lifetime keep ramping
            hasTimeWindow = startTimeMin != 0f || startTimeMax != 0f || endTimeMin != 1f || endTimeMax != 1f || !proportional;

            (clampMin, clampMax) = outputField.SetFloatRange();
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var ages = current.ReadAges();

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var ids = current.ReadIntegers(ParticleField.ParticleId);
            var output = current.Floats(outputField);
            var random = particleSystemState.Random;

            // The rate is a lookup keyed by particle id, so it is only looked up inside the window
            for (var i = 0; i < output.Length; i++)
            {
                var lifetime = lifetimes[i];

                if (lifetime <= 0f)
                {
                    continue;
                }

                if (hasTimeWindow)
                {
                    var age = ages[i];
                    var windowTime = proportional ? ParticleMath.NormalizedAge(age, lifetime) : age;
                    var startTime = random.ForParticleBetween(ids[i], StartTimeOffset, startTimeMin, startTimeMax);
                    var endTime = random.ForParticleBetween(ids[i], EndTimeOffset, endTimeMin, endTimeMax);

                    if (windowTime < startTime || windowTime >= endTime)
                    {
                        continue;
                    }
                }

                var rate = random.ForParticleBetween(ids[i], RateOffset, rateMin, rateMax);
                output[i] = Math.Clamp(output[i] + (rate * frameTime * strength), clampMin, clampMax);
            }
        }
    }
}
