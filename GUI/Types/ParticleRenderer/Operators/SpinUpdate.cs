using System;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SpinUpdate : IParticleOperator
    {
        public SpinUpdate()
        {
        }

        // This is the only place that will update Rotation based on RotationSpeed
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                particle.Rotation += particle.RotationSpeed * frameTime;
            }
        }
    }
}
