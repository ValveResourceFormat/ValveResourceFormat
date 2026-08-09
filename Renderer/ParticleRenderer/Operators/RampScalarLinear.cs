namespace ValveResourceFormat.Renderer.Particles.Operators
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
        private readonly ParticleField field = ParticleField.Radius;

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
            field = parse.ParticleField("m_nField", field);

            // A window spanning the whole normalized lifetime is not tested at all, so particles
            // past their lifetime keep ramping
            hasTimeWindow = startTimeMin != 0f || startTimeMax != 0f || endTimeMin != 1f || endTimeMax != 1f || !proportional;

            (clampMin, clampMax) = field switch
            {
                ParticleField.Alpha or ParticleField.AlphaAlternate => (0f, 1f),
                ParticleField.Radius or ParticleField.TrailLength => (0f, float.MaxValue),
                _ => (-float.MaxValue, float.MaxValue),
            };
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                if (particle.Lifetime <= 0f)
                {
                    continue;
                }

                if (hasTimeWindow)
                {
                    var time = proportional ? particle.NormalizedAge : particle.Age;
                    var startTime = particleSystemState.Random.ForParticleBetween(particle.ParticleId, StartTimeOffset, startTimeMin, startTimeMax);
                    var endTime = particleSystemState.Random.ForParticleBetween(particle.ParticleId, EndTimeOffset, endTimeMin, endTimeMax);

                    if (time < startTime || time >= endTime)
                    {
                        continue;
                    }
                }

                var rate = particleSystemState.Random.ForParticleBetween(particle.ParticleId, RateOffset, rateMin, rateMax);
                var value = particle.GetScalar(field) + (rate * frameTime * strength);

                particle.SetScalar(field, Math.Clamp(value, clampMin, clampMax));
            }
        }
    }
}
