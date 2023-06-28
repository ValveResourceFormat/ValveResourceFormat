using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class MaxVelocity : IParticleOperator
    {
        private readonly float maxVelocity;
        private readonly int overrideCP = -1;
        private readonly int overrideCPField;

        public MaxVelocity(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flMaxVelocity"))
            {
                maxVelocity = keyValues.GetFloatProperty("m_flMaxVelocity");
            }

            if (keyValues.ContainsKey("m_nOverrideCP"))
            {
                overrideCP = keyValues.GetInt32Property("m_nOverrideCP");
            }

            if (keyValues.ContainsKey("m_nOverrideCPField"))
            {
                overrideCPField = keyValues.GetInt32Property("m_nOverrideCPField");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var maxVelocity = this.maxVelocity;
            if (overrideCP > -1)
            {
                var controlPoint = particleSystemState.GetControlPoint(overrideCP);

                maxVelocity = controlPoint.Position.GetComponent(overrideCPField);
            }

            foreach (ref var particle in particles)
            {
                if (particle.Velocity.Length() > maxVelocity)
                {
                    particle.Velocity = Vector3.Normalize(particle.Velocity) * maxVelocity;
                }
            }
        }
    }
}
