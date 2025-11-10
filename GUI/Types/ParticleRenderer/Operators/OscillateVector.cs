using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class OscillateVector : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 RateMin;
        private readonly Vector3 RateMax;
        private readonly Vector3 FrequencyMin = Vector3.One;
        private readonly Vector3 FrequencyMax = Vector3.One;
        private readonly float oscillationMultiplier = 2.0f;
        private readonly float oscillationOffset = 0.5f;
        private readonly bool proportional = true;
        private readonly bool proportionalOp = true;

        private readonly float startTimeMin;
        private readonly float startTimeMax;
        private readonly float endTimeMin = 1.0f;
        private readonly float endTimeMax = 1.0f;

        public OscillateVector(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            RateMin = parse.Vector3("m_RateMin", RateMin);
            RateMax = parse.Vector3("m_RateMax", RateMax);
            FrequencyMin = parse.Vector3("m_FrequencyMin", FrequencyMin);
            FrequencyMax = parse.Vector3("m_FrequencyMax", FrequencyMax);
            oscillationMultiplier = parse.Float("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.Float("m_flOscAdd", oscillationOffset);
            proportional = parse.Boolean("m_bProportional", proportional);
            proportionalOp = parse.Boolean("m_bProportionalOp", proportionalOp);
            startTimeMin = parse.Float("m_flStartTime_min", startTimeMin);
            startTimeMax = parse.Float("m_flStartTime_max", startTimeMax);
            endTimeMin = parse.Float("m_flEndTime_min", endTimeMin);
            endTimeMax = parse.Float("m_flEndTime_max", endTimeMax);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // Remove expired particles
            /*var particlesToRemove = particleRates.Keys.Except(particle).ToList();
            foreach (var p in particlesToRemove)
            {
                particleRates.Remove(p);
                particleFrequencies.Remove(p);
            }*/

            // Update remaining particles
            foreach (ref var particle in particles.Current)
            {
                var rate = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, RateMin, RateMax);
                var frequency = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, FrequencyMin, FrequencyMax);

                var t = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                if (particleSystemState.Data?.BehaviorVersion == 10)
                {
                    var startTime = ParticleCollection.RandomBetween(particle.ParticleID, startTimeMin, startTimeMax);
                    var endTime = ParticleCollection.RandomBetween(particle.ParticleID, endTimeMin, endTimeMax);

                    if (t < startTime)
                    {
                        t = startTime;
                    }
                    else if (t > endTime)
                    {
                        t = endTime;
                    }
                    else
                    {

                    }
                    // todo. refer to fadeandkill
                }

                Vector3 delta;
                delta.X = MathF.Sin(((t * frequency.X * oscillationMultiplier) + oscillationOffset) * MathF.PI);
                delta.Y = MathF.Sin(((t * frequency.Y * oscillationMultiplier) + oscillationOffset) * MathF.PI);
                delta.Z = MathF.Sin(((t * frequency.Z * oscillationMultiplier) + oscillationOffset) * MathF.PI);

                var value = rate * frameTime * delta;

                particle.SetVector(outputField, particle.GetVector(outputField) + value);
            }
        }
    }

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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                Vector3 delta;
                delta.X = MathF.Sin(((particle.Age * frequency.X * oscillationMultiplier) + oscillationOffset) * MathF.PI);
                delta.Y = MathF.Sin(((particle.Age * frequency.Y * oscillationMultiplier) + oscillationOffset) * MathF.PI);
                delta.Z = MathF.Sin(((particle.Age * frequency.Z * oscillationMultiplier) + oscillationOffset) * MathF.PI);

                var value = rate * frameTime * delta;

                particle.SetVector(outputField, particle.GetVector(outputField) + value);
            }
        }
    }

}
