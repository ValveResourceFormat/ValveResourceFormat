using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class LerpScalar : IParticleOperator
    {
        private readonly ParticleField FieldOutput = ParticleField.Radius;
        private readonly INumberProvider output = new LiteralNumberProvider(1);
        private readonly float startTime;
        private readonly float endTime = 1f;

        public LerpScalar(ParticleDefinitionParser parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            output = parse.NumberProvider("m_flOutput", output);
            startTime = parse.Float("m_flStartTime", startTime);
            endTime = parse.Float("m_flEndTime", endTime);
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var lerpTarget = output.NextNumber(ref particle, particleSystemState);

                var lerpWeight = MathUtils.Saturate(MathUtils.Remap(particle.Age, startTime, endTime));

                var scalarOutput = MathUtils.Lerp(lerpWeight, particle.GetInitialScalar(FieldOutput), lerpTarget);

                particle.SetScalar(FieldOutput, scalarOutput);
            }
        }
    }
}
