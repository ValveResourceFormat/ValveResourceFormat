using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    /// <summary>
    /// Cull particle when its alpha is below a certain threshold.
    /// </summary>
    class AlphaDecay : IParticleOperator
    {
        private readonly float minAlpha;
        public AlphaDecay(ParticleDefinitionParser parse)
        {
            minAlpha = parse.Float("m_flMinAlpha", minAlpha);
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                if (particle.Alpha <= minAlpha)
                {
                    particle.Kill();
                }
            }
        }
    }
}
