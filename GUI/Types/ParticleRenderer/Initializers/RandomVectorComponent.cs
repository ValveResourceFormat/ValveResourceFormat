using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomVectorComponent : IParticleInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly float min;
        private readonly float max;
        private readonly int component;

        public RandomVectorComponent(ParticleDefinitionParser parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);

            min = parse.Float("m_flMin", min);

            max = parse.Float("m_flMax", max);

            component = parse.Int32("m_nComponent", component);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var newComponent = MathUtils.RandomBetween(min, max);

            particle.SetVectorComponent(FieldOutput, newComponent, component);

            return particle;
        }
    }
}
