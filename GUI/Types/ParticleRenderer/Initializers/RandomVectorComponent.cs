using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomVectorComponent : IParticleInitializer
    {
        private readonly ParticleField field = ParticleField.Position;
        private readonly float min;
        private readonly float max;
        private readonly int component;

        public RandomVectorComponent(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                field = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_flMin"))
            {
                min = keyValues.GetFloatProperty("m_flMin");
            }

            if (keyValues.ContainsKey("m_flMax"))
            {
                max = keyValues.GetFloatProperty("m_flMax");
            }

            if (keyValues.ContainsKey("m_nComponent"))
            {
                component = keyValues.GetInt32Property("m_nComponent");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var newComponent = MathUtils.RandomBetween(min, max);

            particle.SetVectorComponent(field, newComponent, component);

            return particle;
        }
    }
}
