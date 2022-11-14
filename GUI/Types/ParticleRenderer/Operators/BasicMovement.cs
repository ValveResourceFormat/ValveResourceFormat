using System;
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

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var acceleration = gravity * frameTime;

            for (var i = 0; i < particles.Length; ++i)
            {
                // Apply acceleration
                particles[i].Velocity += acceleration;

                // Apply drag
                particles[i].Velocity *= 1 - (drag * 30f * frameTime);

                particles[i].Position += particles[i].Velocity * frameTime;
            }
        }
    }
}
