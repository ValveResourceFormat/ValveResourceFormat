using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class OscillateScalar : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Alpha;
        private readonly float rateMin;
        private readonly float rateMax;
        private readonly float frequencyMin = 1f;
        private readonly float frequencyMax = 1f;
        private readonly float oscillationMultiplier = 2f;
        private readonly float oscillationOffset = 0.5f;
        private readonly bool proportional = true;

        public OscillateScalar(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            rateMin = parse.Float("m_RateMin", rateMin);
            rateMax = parse.Float("m_RateMax", rateMax);
            frequencyMin = parse.Float("m_FrequencyMin", frequencyMin);
            frequencyMax = parse.Float("m_FrequencyMax", frequencyMax);
            oscillationMultiplier = parse.Float("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.Float("m_flOscAdd", oscillationOffset);
            proportional = parse.Boolean("m_bProportionalOp", proportional);
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
                var rate = ParticleCollection.RandomBetween(particle.ParticleID, rateMin, rateMax);
                var frequency = ParticleCollection.RandomBetween(particle.ParticleID, frequencyMin, frequencyMax);

                var t = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                var delta = MathF.Sin(((t * frequency * oscillationMultiplier) + oscillationOffset) * MathF.PI);

                var finalScalar = delta * rate * frameTime;
                particle.SetScalar(outputField, particle.GetScalar(outputField) + finalScalar);
            }
        }
    }

    class OscillateScalarSimple : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Alpha;
        private readonly float rate;
        private readonly float frequency = 1f;
        private readonly float oscillationMultiplier = 2f;
        private readonly float oscillationOffset = 0.5f;

        public OscillateScalarSimple(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nField", outputField);
            rate = parse.Float("m_Rate", rate);
            frequency = parse.Float("m_Frequency", frequency);
            oscillationMultiplier = parse.Float("m_flOscMult", oscillationMultiplier);
            oscillationOffset = parse.Float("m_flOscAdd", oscillationOffset);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // Update remaining particles
            foreach (ref var particle in particles.Current)
            {
                var delta = MathF.Sin(((particle.Age * frequency * oscillationMultiplier) + oscillationOffset) * MathF.PI);

                var finalScalar = delta * rate * frameTime;

                particle.SetScalar(outputField, particle.GetScalar(outputField) + finalScalar);
            }
        }
    }
}
