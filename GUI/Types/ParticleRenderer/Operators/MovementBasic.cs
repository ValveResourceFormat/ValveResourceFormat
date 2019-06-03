using System;
using System.Collections.Generic;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    public class MovementBasic : IParticleOperator
    {
        public Vector3 Gravity { get; }

        public MovementBasic(IKeyValueCollection keyValues)
        {
            var vectorValues = keyValues.GetArray<float>("m_Gravity");
            Gravity = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]);
        }

        public void Update(IEnumerable<Particle> particles, float frameTime)
        {
            var acceleration = Gravity * frameTime;

            foreach (var particle in particles)
            {
                particle.Velocity += acceleration;

                particle.Position += particle.Velocity;
            }
        }
    }
}
