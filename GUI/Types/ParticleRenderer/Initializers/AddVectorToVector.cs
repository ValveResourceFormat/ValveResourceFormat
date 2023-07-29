using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class AddVectorToVector : IParticleInitializer
    {
        private readonly ParticleField FieldInput = ParticleField.Position;
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly Vector3 OffetMin = Vector3.Zero;
        private readonly Vector3 OffsetMax = Vector3.One;

        public AddVectorToVector(ParticleDefinitionParser parse)
        {
            FieldInput = parse.ParticleField("m_nFieldInput", FieldInput);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            OffetMin = parse.Vector3("m_vOffsetMin", OffetMin);
            OffsetMax = parse.Vector3("m_vOffsetMax", OffsetMax);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetVector(FieldInput);
            var output = particle.GetVector(FieldOutput);

            var offset = MathUtils.RandomBetweenPerComponent(OffetMin, OffsetMax);

            particle.SetInitialVector(FieldOutput, input + output + offset);

            return particle;
        }
    }
}
