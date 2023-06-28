using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class AddVectorToVector : IParticleInitializer
    {
        private readonly ParticleField inputField = ParticleField.Position;
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.One;

        public AddVectorToVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldInput"))
            {
                inputField = keyValues.GetParticleField("m_nFieldInput");
            }

            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                outputField = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_vOffsetMin"))
            {
                offsetMin = keyValues.GetArray<double>("m_vOffsetMin").ToVector3();
            }

            if (keyValues.ContainsKey("m_vOffsetMax"))
            {
                offsetMax = keyValues.GetArray<double>("m_vOffsetMax").ToVector3();
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetVector(inputField);
            var output = particle.GetVector(outputField);

            var offset = MathUtils.RandomBetweenPerComponent(offsetMin, offsetMax);

            particle.SetInitialVector(outputField, input + output + offset);

            return particle;
        }
    }
}
