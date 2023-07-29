using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeOutSimple : IParticleOperator
    {
        private readonly float fadeOutTime = 0.25f;
        private readonly ParticleField FieldOutput = ParticleField.Alpha;

        public FadeOutSimple(ParticleDefinitionParser parse)
        {
            fadeOutTime = parse.Float("m_flFadeOutTime", fadeOutTime);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var timeLeft = 1 - particle.NormalizedAge;
                if (timeLeft <= fadeOutTime)
                {
                    var t = timeLeft / fadeOutTime;
                    var newAlpha = t * particle.InitialAlpha;
                    particle.SetScalar(FieldOutput, newAlpha);
                }
            }
        }
    }
}
