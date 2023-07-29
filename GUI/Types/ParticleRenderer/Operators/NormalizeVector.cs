using System;
using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class NormalizeVector : IParticleOperator
    {
        private readonly ParticleField OutputField = ParticleField.Position;
        private readonly float Scale = 1.0f;

        public NormalizeVector(ParticleDefinitionParser parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);

            Scale = parse.Float("m_flScale", Scale);

            // there's also a Lerp value that will fade it in when at low values. Further testing is needed to know anything more
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var vector = particle.GetVector(OutputField);
                vector = Vector3.Normalize(vector) * Scale;

                particle.SetVector(OutputField, vector);
            }
        }
    }
}
