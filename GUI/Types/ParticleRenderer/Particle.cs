using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    public class Particle
    {
        public Vector3 Color { get; set; } = Vector3.One;

        public Vector3 Position { get; set; }

        public float Radius { get; set; } = 5;

        public Vector3 Velocity { get; set; }

        public Particle()
        {
        }

        public Particle(IKeyValueCollection baseProperties)
        {
            if (baseProperties.ContainsKey("m_flConstantRadius"))
            {
                Radius = baseProperties.GetFloatProperty("m_flConstantRadius");
            }
        }
    }
}
