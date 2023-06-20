using System;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SpinUpdate : IParticleOperator
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
