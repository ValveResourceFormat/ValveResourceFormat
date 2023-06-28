using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class OffsetVectorToVector : IParticleInitializer
    {
        private readonly ParticleField inputField = ParticleField.Position;
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.One;

        public OffsetVectorToVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldInput"))
            {
                inputField = keyValues.GetParticleField("m_nFieldInput");
            }

            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                outputField = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_vecOutputMin"))
            {
                offsetMin = keyValues.GetArray<double>("m_vecOutputMin").ToVector3();
            }

            if (keyValues.ContainsKey("m_vecOutputMax"))
            {
                offsetMax = keyValues.GetArray<double>("m_vecOutputMax").ToVector3();
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetInitialVector(inputField);

            var offset = MathUtils.RandomBetweenPerComponent(offsetMin, offsetMax);

            particle.SetInitialVector(outputField, input + offset);

            return particle;
        }
    }
}
