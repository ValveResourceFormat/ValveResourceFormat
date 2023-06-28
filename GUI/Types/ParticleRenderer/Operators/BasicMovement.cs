using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class BasicMovement : IParticleOperator
    {
        private readonly Vector3 gravity = Vector3.Zero;
        private readonly float drag;

        public BasicMovement(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_Gravity"))
            {
                gravity = keyValues.GetArray<double>("m_Gravity").ToVector3();
            }

            if (keyValues.ContainsKey("m_fDrag"))
            {
                drag = keyValues.GetFloatProperty("m_fDrag");
            }
        }

        private static Vector3 GetVelocityFromPreviousPosition(Vector3 currPosition, Vector3 prevPosition)
        {
            var velocity = currPosition - prevPosition;
            return Vector3.Zero; // temp due to weird ordering
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var gravityMovement = gravity * frameTime;

            for (var i = 0; i < particles.Length; ++i)
            {
                // SO. Velocity is partially computed using the previous frame's position.
                // Which means that anything with basicmovement will have additional effects that we
                // aren't accounting for if we don't apply whatever this is.

                // Apply acceleration
                particles[i].Velocity += gravityMovement;

                particles[i].Velocity += GetVelocityFromPreviousPosition(particles[i].Position, particles[i].PositionPrevious);

                // Apply drag
                // Is this right? this is a super important operator so it might be bad if this is wrong
                particles[i].Velocity *= 1 - (drag * 30f * frameTime);

                // Velocity is only applied in MovementBasic. If you layer two MovementBasic's on top of one another it'll indeed apply velocity twice.
                particles[i].Position += particles[i].Velocity * frameTime;
            }
        }
    }
}
