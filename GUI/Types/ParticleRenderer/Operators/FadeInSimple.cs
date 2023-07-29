using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeInSimple : IParticleOperator
    {
        private readonly float fadeInTime = 0.25f;
        private readonly ParticleField FieldOutput = ParticleField.Alpha;

        public FadeInSimple(ParticleDefinitionParser parse)
        {
            fadeInTime = parse.Float("m_flFadeInTime", fadeInTime);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var time = particle.NormalizedAge;
                if (time <= fadeInTime)
                {
                    var newAlpha = (time / fadeInTime) * particle.InitialAlpha;
                    particle.SetScalar(FieldOutput, newAlpha);
                }
            }
        }
    }
}
