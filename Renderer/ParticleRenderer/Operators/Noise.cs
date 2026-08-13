using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Applies noise to a scalar particle attribute, mapping a noise value into a configurable
    /// output range. Can operate additively or by replacing the current value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Noise">C_OP_Noise</seealso>
    class Noise : ParticleFunctionOperator
    {
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly bool additive;
        private readonly float noiseScale = 0.1f;
        private readonly float animationTimeScale;

        public Noise(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            additive = parse.Boolean("m_bAdditive", false);
            noiseScale = parse.Float("m_fl4NoiseScale", noiseScale);
            animationTimeScale = parse.Float("m_flNoiseAnimationTimeScale", animationTimeScale);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var min = outputMin.NextNumber(particleSystemState);
            var max = outputMax.NextNumber(particleSystemState);

            if (outputField.IsAngleField())
            {
                min = float.DegreesToRadians(min);
                max = float.DegreesToRadians(max);
            }

            // Calculate coefficients for noise scaling (noise returns -1..1)
            var valueScale = 0.5f * (max - min);
            var valueBase = min + valueScale;

            if (additive)
            {
                valueScale *= frameTime;
                valueBase *= frameTime;
            }

            var setMethod = additive ? ParticleSetMethod.PARTICLE_SET_ADD_TO_CURRENT_VALUE : ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

            // The animation time offset moves the sample point along X only.
            var timeOffset = animationTimeScale * particleSystemState.Age;

            foreach (ref var particle in particles.Current)
            {
                var samplePoint = particle.Position * noiseScale;
                var noiseValue = Utils.Noise.Value3D(samplePoint.X + timeOffset, samplePoint.Y, samplePoint.Z);

                var finalValue = valueBase + valueScale * noiseValue;
                finalValue = particle.ModifyScalarBySetMethod(particles, outputField, finalValue, setMethod);

                particle.SetScalar(outputField, finalValue);
            }
        }
    }
}
