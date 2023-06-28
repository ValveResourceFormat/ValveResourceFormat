using System;
using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    // seriously?
    class RemapCrossProductOfTwoVectorsToVector : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Position;
        private readonly IVectorProvider inputVec1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider inputVec2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly bool normalize;

        public RemapCrossProductOfTwoVectorsToVector(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                field = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_InputVec1"))
            {
                inputVec1 = keyValues.GetVectorProvider("m_InputVec1");
            }

            if (keyValues.ContainsKey("m_InputVec2"))
            {
                inputVec2 = keyValues.GetVectorProvider("m_InputVec2");
            }

            if (keyValues.ContainsKey("m_bNormalize"))
            {
                normalize = keyValues.GetProperty<bool>("m_bNormalize");
            }
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                var vec1 = inputVec1.NextVector(particle, particleSystemState);
                var vec2 = inputVec2.NextVector(particle, particleSystemState);

                var cross = Vector3.Cross(vec1, vec2);

                if (normalize)
                {
                    cross = Vector3.Normalize(cross);
                }

                particle.SetVector(field, cross);
            }
        }
    }
}
