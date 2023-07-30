using System;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SpinUpdate : IParticleOperator
    {
        public SpinUpdate()
        {
        }

        // This is the only place that will update Rotation based on RotationSpeed
        public void Update(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                particle.Rotation += particle.RotationSpeed * frameTime;
            }
        }
    }
}
