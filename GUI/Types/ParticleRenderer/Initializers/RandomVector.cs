using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomVector : IParticleInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly Vector3 Min;
        private readonly Vector3 Max;

        public RandomVector(ParticleDefinitionParser parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            Min = parse.Vector3("m_vecMin", Min);
            Max = parse.Vector3("m_vecMax", Max);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var newVector = MathUtils.RandomBetweenPerComponent(Min, Max);

            particle.SetVector(FieldOutput, newVector);

            return particle;
        }
    }
}
