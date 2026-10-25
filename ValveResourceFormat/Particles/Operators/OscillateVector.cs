using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
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
        private readonly INumberProvider rateScale = new LiteralNumberProvider(1.0f);
        private readonly bool proportional = true;
        private readonly bool proportionalOp = true;

        private readonly float startTimeMin;
        private readonly float startTimeMax;
        private readonly float endTimeMin = 1.0f;
        private readonly float endTimeMax = 1.0f;

        /// <summary>Whether the output field is one of those normalized to [0, 1].</summary>
        private readonly bool clampToUnit;

        /// <summary>Whether the same delta is also added to the previous position.</summary>
        private readonly bool offsetsPosition;

        public OscillateVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            rateMin = parse.Vector3("m_RateMin", rateMin);
            rateMax = parse.Vector3("m_RateMax", rateMax);
            frequencyMin = parse.Vector3("m_FrequencyMin", frequencyMin);
            frequencyMax = parse.Vector3("m_FrequencyMax", frequencyMax);
            oscillationMultiplier = parse.NumberProvider("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.NumberProvider("m_flOscAdd", oscillationOffset);
            rateScale = parse.NumberProvider("m_flRateScale", rateScale);
            proportional = parse.Boolean("m_bProportional", proportional);
            proportionalOp = parse.Boolean("m_bProportionalOp", proportionalOp);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);

            clampToUnit = outputField.IsNormalizedField();
            offsetsPosition = parse.Boolean("m_bOffset", false) && outputField == ParticleField.Position;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var step = strength * frameTime;

            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var current = particles.Current;
            var values = current.Vectors(outputField);
            var previousPositions = offsetsPosition ? current.Vectors(ParticleField.PositionPrevious) : default;
            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var particleIds = current.ReadIntegers(ParticleField.ParticleId);
            var ages = current.ReadAges();

            var multipliers = oscillationMultiplier.Fill(current, particleSystemState);
            var offsets = oscillationOffset.Fill(current, particleSystemState);
            var rateScales = rateScale.Fill(current, particleSystemState);

            for (var i = 0; i < values.Length; i++)
            {
                var age = ages[i];
                var lifetime = lifetimes[i];

                if (lifetime <= 0f)
                {
                    continue;
                }

                var particleId = particleIds[i];
                var startTime = particleSystemState.Random.ForParticleBetween(particleId, StartTimeOffset, startTimeMin, startTimeMax);
                var endTime = particleSystemState.Random.ForParticleBetween(particleId, EndTimeOffset, endTimeMin, endTimeMax);

                var normalizedAge = ParticleMath.NormalizedAge(age, lifetime);
                var windowTime = proportionalOp ? normalizedAge : age;

                if (windowTime < startTime || windowTime >= endTime)
                {
                    continue;
                }

                var rate = new Vector3(
                    particleSystemState.Random.ForParticleBetween(particleId, RateOffsetX, rateMin.X, rateMax.X),
                    particleSystemState.Random.ForParticleBetween(particleId, RateOffsetY, rateMin.Y, rateMax.Y),
                    particleSystemState.Random.ForParticleBetween(particleId, RateOffsetZ, rateMin.Z, rateMax.Z));

                var frequency = new Vector3(
                    particleSystemState.Random.ForParticleBetween(particleId, FrequencyOffsetX, frequencyMin.X, frequencyMax.X),
                    particleSystemState.Random.ForParticleBetween(particleId, FrequencyOffsetY, frequencyMin.Y, frequencyMax.Y),
                    particleSystemState.Random.ForParticleBetween(particleId, FrequencyOffsetZ, frequencyMin.Z, frequencyMax.Z));

                Vector3 phase;

                if (proportional)
                {
                    phase = (normalizedAge * frequency * multipliers[i]) + new Vector3(offsets[i]);
                }
                else
                {
                    phase = frequency * ((multipliers[i] * particleSystemState.Age) + offsets[i]);
                }

                var delta = new Vector3(ParticleMath.SinPi(phase.X), ParticleMath.SinPi(phase.Y), ParticleMath.SinPi(phase.Z));
                var value = rate * (step * rateScales[i]) * delta;
                var oscillated = values[i] + value;

                if (clampToUnit)
                {
                    oscillated = new Vector3(MathUtils.Saturate(oscillated.X), MathUtils.Saturate(oscillated.Y), MathUtils.Saturate(oscillated.Z));
                }

                values[i] = oscillated;

                if (offsetsPosition)
                {
                    previousPositions[i] += value;
                }
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

        /// <summary>Whether the output field is one of those normalized to [0, 1].</summary>
        private readonly bool clampToUnit;

        /// <summary>Whether the same delta is also added to the previous position.</summary>
        private readonly bool offsetsPosition;

        private readonly ulong storedFields;

        public OscillateVectorSimple(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            rate = parse.Vector3("m_Rate", rate);
            frequency = parse.Vector3("m_Frequency", frequency);
            oscillationMultiplier = parse.Float("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.Float("m_flOscAdd", oscillationOffset);

            clampToUnit = outputField.IsNormalizedField();

            var offset = parse.Boolean("m_bOffset", false);
            offsetsPosition = offset && outputField == ParticleField.Position;
            storedFields = outputField.Mask();

            if (offset)
            {
                storedFields |= ParticleField.PositionPrevious.Mask();
            }
        }

        /// <inheritdoc/>
        public override ulong StoredFields => storedFields;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            // The frequency scales the whole phase, offset included, and the phase runs off the system
            // clock rather than particle age, so every particle takes the same delta.
            var phase = (oscillationMultiplier * particleSystemState.Age) + oscillationOffset;
            var step = strength * frameTime;

            var delta = new Vector3(
                ParticleMath.SinPi(frequency.X * phase),
                ParticleMath.SinPi(frequency.Y * phase),
                ParticleMath.SinPi(frequency.Z * phase));

            var value = rate * step * delta;

            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var current = particles.Current;
            var values = current.Vectors(outputField);
            var previousPositions = offsetsPosition ? current.Vectors(ParticleField.PositionPrevious) : default;

            for (var i = 0; i < values.Length; i++)
            {
                var oscillated = values[i] + value;

                values[i] = clampToUnit ? Vector3.Clamp(oscillated, Vector3.Zero, Vector3.One) : oscillated;

                if (offsetsPosition)
                {
                    previousPositions[i] += value;
                }
            }
        }
    }
}
