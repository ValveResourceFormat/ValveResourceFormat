using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomScalar : IParticleInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Radius;
        private readonly float scalarMin;
        private readonly float scalarMax;
        private readonly float exponent = 1;

        public RandomScalar(ParticleDefinitionParser parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            scalarMin = parse.Float("m_flMin", scalarMin);
            scalarMax = parse.Float("m_flMax", scalarMax);
            scalarMax = parse.Float("m_flExponent", scalarMax);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var value = MathUtils.RandomWithExponentBetween(exponent, scalarMin, scalarMax);

            particle.SetInitialScalar(FieldOutput, value);

            return particle;
        }
    }
}
