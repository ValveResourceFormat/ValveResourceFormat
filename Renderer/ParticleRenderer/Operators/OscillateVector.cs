namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Oscillates a vector particle attribute by adding a per-component sinusoidal delta each
    /// frame. The oscillation rate and frequency vectors are randomized per particle within
    /// configurable min/max ranges.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_OscillateVector">C_OP_OscillateVector</seealso>
    class OscillateVector : ParticleFunctionOperator
    {
        /// <summary>
        /// Table offsets separating this operator's draws. The Y frequency and the end time read the
        /// same slot.
        /// </summary>
        private const int RateOffsetX = 3;
        private const int RateOffsetY = 7;
        private const int RateOffsetZ = 9;
        private const int FrequencyOffsetX = 8;
        private const int FrequencyOffsetY = 12;
        private const int FrequencyOffsetZ = 15;
        private const int StartTimeOffset = 11;
        private const int EndTimeOffset = 12;

        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 rateMin;
        private readonly Vector3 rateMax;
        private readonly Vector3 frequencyMin = Vector3.One;
        private readonly Vector3 frequencyMax = Vector3.One;
        private readonly INumberProvider oscillationMultiplier = new LiteralNumberProvider(2.0f);
        private readonly INumberProvider oscillationOffset = new LiteralNumberProvider(0.5f);
        private readonly bool proportional = true;
        private readonly bool proportionalOp = true;

        private readonly float startTimeMin;
        private readonly float startTimeMax;
        private readonly float endTimeMin = 1.0f;
        private readonly float endTimeMax = 1.0f;

        public OscillateVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            rateMin = parse.Vector3("m_RateMin", rateMin);
            rateMax = parse.Vector3("m_RateMax", rateMax);
            frequencyMin = parse.Vector3("m_FrequencyMin", frequencyMin);
            frequencyMax = parse.Vector3("m_FrequencyMax", frequencyMax);
            oscillationMultiplier = parse.NumberProvider("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.NumberProvider("m_flOscAdd", oscillationOffset);
            proportional = parse.Boolean("m_bProportional", proportional);
            proportionalOp = parse.Boolean("m_bProportionalOp", proportionalOp);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var windowTime = proportionalOp
                    ? particle.NormalizedAge
                    : particle.Age;

                var startTime = particleSystemState.Random.ForParticleBetween(particle.ParticleId, StartTimeOffset, startTimeMin, startTimeMax);
                var endTime = particleSystemState.Random.ForParticleBetween(particle.ParticleId, EndTimeOffset, endTimeMin, endTimeMax);

                if (windowTime < startTime || windowTime >= endTime)
                {
                    continue;
                }

                var rate = new Vector3(
                    particleSystemState.Random.ForParticleBetween(particle.ParticleId, RateOffsetX, rateMin.X, rateMax.X),
                    particleSystemState.Random.ForParticleBetween(particle.ParticleId, RateOffsetY, rateMin.Y, rateMax.Y),
                    particleSystemState.Random.ForParticleBetween(particle.ParticleId, RateOffsetZ, rateMin.Z, rateMax.Z));

                var frequency = new Vector3(
                    particleSystemState.Random.ForParticleBetween(particle.ParticleId, FrequencyOffsetX, frequencyMin.X, frequencyMax.X),
                    particleSystemState.Random.ForParticleBetween(particle.ParticleId, FrequencyOffsetY, frequencyMin.Y, frequencyMax.Y),
                    particleSystemState.Random.ForParticleBetween(particle.ParticleId, FrequencyOffsetZ, frequencyMin.Z, frequencyMax.Z));

                var t = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                var multiplier = oscillationMultiplier.NextNumber(ref particle, particleSystemState);
                var offset = oscillationOffset.NextNumber(ref particle, particleSystemState);

                Vector3 delta;
                delta.X = float.SinPi((t * frequency.X * multiplier) + offset);
                delta.Y = float.SinPi((t * frequency.Y * multiplier) + offset);
                delta.Z = float.SinPi((t * frequency.Z * multiplier) + offset);

                var value = rate * frameTime * strength * delta;

                particle.SetVector(outputField, particle.GetVector(outputField) + value);
            }
        }
    }

    /// <summary>
    /// Oscillates a vector particle attribute by adding a per-component sinusoidal delta each
    /// frame, using fixed rate and frequency vectors.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_OscillateVectorSimple">C_OP_OscillateVectorSimple</seealso>
    class OscillateVectorSimple : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 rate;
        private readonly Vector3 frequency = Vector3.One;
        private readonly float oscillationMultiplier = 2.0f;
        private readonly float oscillationOffset = 0.5f;

        public OscillateVectorSimple(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            rate = parse.Vector3("m_Rate", rate);
            frequency = parse.Vector3("m_Frequency", frequency);
            oscillationMultiplier = parse.Float("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.Float("m_flOscAdd", oscillationOffset);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                Vector3 delta;
                delta.X = float.SinPi((particle.Age * frequency.X * oscillationMultiplier) + oscillationOffset);
                delta.Y = float.SinPi((particle.Age * frequency.Y * oscillationMultiplier) + oscillationOffset);
                delta.Z = float.SinPi((particle.Age * frequency.Z * oscillationMultiplier) + oscillationOffset);

                var value = rate * frameTime * delta;

                particle.SetVector(outputField, particle.GetVector(outputField) + value);
            }
        }
    }

}
