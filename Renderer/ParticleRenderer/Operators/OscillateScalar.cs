namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Oscillates a scalar particle attribute by adding a sinusoidal delta each frame. The
    /// oscillation rate and frequency are randomized per particle within configurable min/max ranges,
    /// as is the window of the particle's life over which the oscillation runs.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_OscillateScalar">C_OP_OscillateScalar</seealso>
    class OscillateScalar : ParticleFunctionOperator
    {
        /// <summary>Table offsets separating this operator's frequency, rate and window draws.</summary>
        private const int FrequencyOffset = 0;
        private const int RateOffset = 1;
        private const int StartTimeOffset = 11;
        private const int EndTimeOffset = 12;

        private readonly ParticleField outputField = ParticleField.Alpha;
        private readonly float rateMin;
        private readonly float rateMax;
        private readonly float frequencyMin = 1f;
        private readonly float frequencyMax = 1f;
        private readonly float startTimeMin;
        private readonly float startTimeMax;
        private readonly float endTimeMin = 1f;
        private readonly float endTimeMax = 1f;
        private readonly float oscillationMultiplier = 2f;
        private readonly float oscillationOffset = 0.5f;
        private readonly bool proportional = true;

        /// <summary>Whether the active window is expressed as a fraction of the particle's lifetime rather than in seconds.</summary>
        private readonly bool proportionalWindow = true;

        public OscillateScalar(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            rateMin = parse.Float("m_RateMin", rateMin);
            rateMax = parse.Float("m_RateMax", rateMax);
            frequencyMin = parse.Float("m_FrequencyMin", frequencyMin);
            frequencyMax = parse.Float("m_FrequencyMax", frequencyMax);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
            oscillationMultiplier = parse.Float("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.Float("m_flOscAdd", oscillationOffset);
            proportional = parse.Boolean("m_bProportional", proportional);
            proportionalWindow = parse.Boolean("m_bProportionalOp", proportionalWindow);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var windowTime = proportionalWindow
                    ? particle.NormalizedAge
                    : particle.Age;

                var startTime = particleSystemState.Random.ForParticleBetween(particle.ParticleId, StartTimeOffset, startTimeMin, startTimeMax);
                var endTime = particleSystemState.Random.ForParticleBetween(particle.ParticleId, EndTimeOffset, endTimeMin, endTimeMax);

                if (windowTime < startTime || windowTime >= endTime)
                {
                    continue;
                }

                var frequency = particleSystemState.Random.ForParticleBetween(particle.ParticleId, FrequencyOffset, frequencyMin, frequencyMax);
                var rate = particleSystemState.Random.ForParticleBetween(particle.ParticleId, RateOffset, rateMin, rateMax);

                var t = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                var delta = float.SinPi((t * frequency * oscillationMultiplier) + oscillationOffset);

                var finalScalar = delta * rate * frameTime * strength;
                particle.SetScalar(outputField, particle.GetScalar(outputField) + finalScalar);
            }
        }
    }

    /// <summary>
    /// Oscillates a scalar particle attribute by adding a sinusoidal delta each frame, using
    /// fixed rate and frequency values.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_OscillateScalarSimple">C_OP_OscillateScalarSimple</seealso>
    class OscillateScalarSimple : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Alpha;
        private readonly float rate;
        private readonly float frequency = 1f;
        private readonly float oscillationMultiplier = 2f;
        private readonly float oscillationOffset = 0.5f;
        private readonly float clampMin;
        private readonly float clampMax;

        public OscillateScalarSimple(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            rate = parse.Float("m_Rate", rate);
            frequency = parse.Float("m_Frequency", frequency);
            oscillationMultiplier = parse.Float("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.Float("m_flOscAdd", oscillationOffset);

            (clampMin, clampMax) = outputField switch
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
                var delta = float.SinPi((particle.Age * frequency * oscillationMultiplier) + oscillationOffset);

                var finalScalar = delta * rate * frameTime;

                var oscillated = particle.GetScalar(outputField) + finalScalar;
                particle.SetScalar(outputField, Math.Clamp(oscillated, clampMin, clampMax));
            }
        }
    }
}
