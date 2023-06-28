using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomVector : IParticleInitializer
    {
        private readonly ParticleField field = ParticleField.Position;
        private readonly Vector3 vectorMin;
        private readonly Vector3 vectorMax;

        public RandomVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                field = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_vecMin"))
            {
                vectorMin = keyValues.GetArray<double>("m_vecMin").ToVector3();
            }

            if (keyValues.ContainsKey("m_vecMax"))
            {
                vectorMax = keyValues.GetArray<double>("m_vecMax").ToVector3();
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var newVector = MathUtils.RandomBetweenPerComponent(vectorMin, vectorMax);

            particle.SetVector(field, newVector);

            return particle;
        }
    }
}
