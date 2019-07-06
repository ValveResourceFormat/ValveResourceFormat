using System.Collections.Generic;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class SpinUpdate : IParticleOperator
    {
        public SpinUpdate(IKeyValueCollection keyValues)
        {
        }

        public void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                particle.Rotation += particle.RotationSpeed * frameTime;
            }
        }
    }
}
