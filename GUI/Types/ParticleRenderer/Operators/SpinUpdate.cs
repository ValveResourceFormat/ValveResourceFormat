using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class SpinUpdate : IParticleOperator
    {
        public SpinUpdate()
        {
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (var i = 0; i < particles.Length; ++i)
            {
                particles[i].Rotation += particles[i].RotationSpeed * frameTime;
            }
        }
    }
}
