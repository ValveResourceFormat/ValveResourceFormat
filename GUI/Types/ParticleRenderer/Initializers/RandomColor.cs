using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomColor : IParticleInitializer
    {
        private readonly Vector3 colorMin = Vector3.One;
        private readonly Vector3 colorMax = Vector3.One;

        public RandomColor(ParticleDefinitionParser parse)
        {
            if (parse.Data.ContainsKey("m_ColorMin"))
            {
                var vectorValues = parse.Data.GetIntegerArray("m_ColorMin");
                colorMin = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            if (parse.Data.ContainsKey("m_ColorMax"))
            {
                var vectorValues = parse.Data.GetIntegerArray("m_ColorMax");
                colorMax = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            // lots of stuff with Tinting in hlvr.
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.InitialColor = MathUtils.RandomBetween(colorMin, colorMax);
            particle.Color = particle.InitialColor;

            return particle;
        }
    }
}
