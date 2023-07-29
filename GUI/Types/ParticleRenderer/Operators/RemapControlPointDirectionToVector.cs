using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class RemapControlPointDirectionToVector : IParticleOperator
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly int cp;
        private readonly float scale;

        public RemapControlPointDirectionToVector(ParticleDefinitionParser parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);

            scale = parse.Float("m_flScale", scale);

            cp = parse.Int32("m_nControlPointNumber", cp);
        }

        // is this particle id or total particle count?
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                // direction or orientation??
                var direction = particleSystemState.GetControlPoint(cp).Orientation;
                particle.SetVector(FieldOutput, direction * scale);
            }
        }
    }
}
