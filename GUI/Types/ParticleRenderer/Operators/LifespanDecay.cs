using System;
using System.Collections.Generic;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class LifespanDecay : IParticleOperator
    {
        public LifespanDecay(IKeyValueCollection keyValues)
        {
        }

        public void Update(IEnumerable<Particle> particles, float frameTime)
        {
            foreach (var particle in particles)
            {
                particle.Lifetime -= frameTime;
            }
        }
    }
}
