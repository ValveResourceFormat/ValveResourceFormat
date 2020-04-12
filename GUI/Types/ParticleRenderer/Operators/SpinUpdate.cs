using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class SpinUpdate : IParticleOperator
    {
#pragma warning disable CA1801
        public SpinUpdate(IKeyValueCollection keyValues)
        {
        }
#pragma warning restore CA1801

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            for (int i = 0; i < particles.Length; ++i)
            {
                particles[i].Rotation += particles[i].RotationSpeed * frameTime;
            }
        }
    }
}
