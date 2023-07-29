using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class OffsetVectorToVector : IParticleInitializer
    {
        private readonly ParticleField FieldInput = ParticleField.Position;
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.One;

        public OffsetVectorToVector(ParticleDefinitionParser parse)
        {
            FieldInput = parse.ParticleField("m_nFieldInput", FieldInput);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);

            if (parse.Data.ContainsKey("m_vecOutputMin"))
            {
                offsetMin = parse.Vector3("m_vecOutputMin", offsetMin);
            }

            if (parse.Data.ContainsKey("m_vecOutputMax"))
            {
                offsetMax = parse.Vector3("m_vecOutputMax", offsetMax);
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetInitialVector(FieldInput);

            var offset = MathUtils.RandomBetweenPerComponent(offsetMin, offsetMax);

            particle.SetInitialVector(FieldOutput, input + offset);

            return particle;
        }
    }
}
