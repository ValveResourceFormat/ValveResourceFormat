using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class Noise : ParticleFunctionOperator
    {
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);
        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly bool Additive;
        //private readonly float noiseScale;
        //private readonly float noiseAnimationTimeScale;

        public Noise(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            //noiseScale = parse.Float("m_fl4NoiseScale", 1.0f);
            //noiseAnimationTimeScale = parse.Float("m_flNoiseAnimationTimeScale", 0.0f);

            Additive = parse.Boolean("m_bAdditive", false);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var min = outputMin.NextNumber(particleSystemState);
            var max = outputMax.NextNumber(particleSystemState);

            if (OutputField.IsAngleField())
            {
                min *= (float)(Math.PI / 180.0);
                max *= (float)(Math.PI / 180.0);
            }

            // Calculate coefficients for noise scaling (noise returns -1..1)
            var valueScale = 0.5f * (max - min);
            var valueBase = min + valueScale;

            if (Additive)
            {
                valueScale *= frameTime;
                valueBase *= frameTime;
            }

            var setMethod = Additive ? ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE : ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

            foreach (ref var particle in particles.Current)
            {
                // Valve uses NoiseSIMD which uses much more complicated deterministic noise based on coord
                //var coord = particle.Position * noiseScale;

                var noiseValue = ParticleCollection.RandomBetween(particle.ParticleID, -1f, 1f);

                var finalValue = valueBase + valueScale * noiseValue;
                finalValue = particle.ModifyScalarBySetMethod(particles, OutputField, finalValue, setMethod);

                particle.SetScalar(OutputField, finalValue);
            }
        }
    }
}
