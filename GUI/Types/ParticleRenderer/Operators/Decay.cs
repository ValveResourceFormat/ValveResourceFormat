using System;
using System.Collections.Generic;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class Decay : IParticleOperator
    {
        public Decay(IKeyValueCollection keyValues)
        {
        }

        public void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                particle.Lifetime -= frameTime;
            }
        }
    }
}
