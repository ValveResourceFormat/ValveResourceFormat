using System.Collections.Generic;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class BasicMovement : IParticleOperator
    {
        private readonly Vector3 gravity;
        private readonly float drag;

        public BasicMovement(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_Gravity"))
            {
                var vectorValues = keyValues.GetArray<double>("m_Gravity");
                gravity = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }

            if (keyValues.ContainsKey("m_fDrag"))
            {
                drag = keyValues.GetFloatProperty("m_fDrag");
            }
        }

        public void Update(IEnumerable<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var acceleration = gravity * frameTime;

            foreach (var particle in particles)
            {
                // Apply acceleration
                particle.Velocity += acceleration;

                // Apply drag
                particle.Velocity *= 1 - (drag * 30f * frameTime);

                particle.Position += particle.Velocity * frameTime;
            }
        }
    }
}
