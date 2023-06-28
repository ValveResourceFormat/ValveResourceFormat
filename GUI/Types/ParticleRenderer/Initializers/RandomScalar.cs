using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomScalar : IParticleInitializer
    {
        private readonly ParticleField field = ParticleField.Radius;
        private readonly float scalarMin;
        private readonly float scalarMax;
        private readonly float exponent = 1;

        public RandomScalar(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                field = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_flMin"))
            {
                scalarMin = keyValues.GetFloatProperty("m_flMin");
            }

            if (keyValues.ContainsKey("m_flMax"))
            {
                scalarMax = keyValues.GetFloatProperty("m_flMax");
            }

            if (keyValues.ContainsKey("m_flExponent"))
            {
                scalarMax = keyValues.GetFloatProperty("m_flExponent");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var value = MathUtils.RandomWithExponentBetween(exponent, scalarMin, scalarMax);

            particle.SetInitialScalar(field, value);

            return particle;
        }
    }
}
