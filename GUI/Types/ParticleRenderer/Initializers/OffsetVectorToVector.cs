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
        private readonly Vector3 OutputMin = Vector3.Zero;
        private readonly Vector3 OutputMax = Vector3.One;

        public OffsetVectorToVector(ParticleDefinitionParser parse)
        {
            FieldInput = parse.ParticleField("m_nFieldInput", FieldInput);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            OutputMin = parse.Vector3("m_vecOutputMin", OutputMin);
            OutputMax = parse.Vector3("m_vecOutputMax", OutputMax);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetInitialVector(FieldInput);

            var offset = MathUtils.RandomBetweenPerComponent(OutputMin, OutputMax);

            particle.SetInitialVector(FieldOutput, input + offset);

            return particle;
        }
    }
}
