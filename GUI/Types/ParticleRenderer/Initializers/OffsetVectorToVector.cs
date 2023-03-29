using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class OffsetVectorToVector : IParticleInitializer
    {
        private readonly ParticleField inputField = ParticleField.Position;
        private readonly ParticleField outputField = ParticleField.Position;
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.One;

        public OffsetVectorToVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldInput"))
            {
                inputField = (ParticleField)keyValues.GetIntegerProperty("m_nFieldInput");
            }

            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                outputField = (ParticleField)keyValues.GetIntegerProperty("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_vecOutputMin"))
            {
                var vectorValues = keyValues.GetArray<double>("m_vecOutputMin");
                offsetMin = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }

            if (keyValues.ContainsKey("m_vecOutputMax"))
            {
                var vectorValues = keyValues.GetArray<double>("m_vecOutputMax");
                offsetMax = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetVector(inputField);

            var offset = new Vector3(
                Lerp(offsetMin.X, offsetMax.X, (float)Random.Shared.NextDouble()),
                Lerp(offsetMin.Y, offsetMax.Y, (float)Random.Shared.NextDouble()),
                Lerp(offsetMin.Z, offsetMax.Z, (float)Random.Shared.NextDouble()));

            if (outputField == ParticleField.Position)
            {
                particle.Position += input + offset;
            }
            else if (outputField == ParticleField.PositionPrevious)
            {
                particle.PositionPrevious = input + offset;
            }

            return particle;
        }

        private static float Lerp(float min, float max, float t)
            => min + (t * (max - min));
    }
}
