using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Reads a vector from an input field, adds it to the existing output field vector plus a
    /// per-component random offset, and writes the sum back into the output field. An offset range
    /// that is zero at both ends takes no random draw.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_AddVectorToVector">C_INIT_AddVectorToVector</seealso>
    class AddVectorToVector : ParticleFunctionInitializer
    {
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly ParticleField fieldOutput = ParticleField.Position;
        private readonly Vector3 offsetMin = Vector3.Zero;
        private readonly Vector3 offsetMax = Vector3.Zero;
        private readonly RangeSampler rangeSampler;

        public AddVectorToVector(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            offsetMin = parse.Vector3("m_vOffsetMin", offsetMin);
            offsetMax = parse.Vector3("m_vOffsetMax", offsetMax);
            rangeSampler = RangeSampler.Parse(parse);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetVector(fieldInput);
            var output = particle.GetVector(fieldOutput);

            if (offsetMin == Vector3.Zero && offsetMax == Vector3.Zero)
            {
                particle.SetVector(fieldOutput, input + output);

                return particle;
            }

            var offset = rangeSampler.NextVectorBetween(ref particle, particleSystemState, offsetMin, offsetMax);

            particle.SetVector(fieldOutput, input + output + offset);

            return particle;
        }
    }
}
