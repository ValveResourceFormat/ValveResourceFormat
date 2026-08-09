using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Reads a vector from an input field and writes it plus a per-component random offset into an
    /// output field. Unlike <see cref="AddVectorToVector"/>, the output field's existing value is
    /// not included in the sum.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_OffsetVectorToVector">C_INIT_OffsetVectorToVector</seealso>
    class OffsetVectorToVector : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly RangeSampler rangeSampler;

        public OffsetVectorToVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);
            rangeSampler = RangeSampler.Parse(parse);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetVector(fieldInput);

            var offset = rangeSampler.NextVectorBetween(ref particle, particleSystemState, outputMin, outputMax);

            particle.SetVector(fieldOutput, input + offset);

            return particle;
        }
    }
}
