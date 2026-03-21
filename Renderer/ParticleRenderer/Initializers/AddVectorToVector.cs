namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Reads a vector from an input field, adds a per-component random offset to it, and writes the
    /// result into an output field.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_AddVectorToVector">C_INIT_AddVectorToVector</seealso>
    class AddVectorToVector : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldInput = ParticleField.Position;
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly Vector3 OffetMin = Vector3.Zero;
        private readonly Vector3 OffsetMax = Vector3.One;

        public AddVectorToVector(ParticleDefinitionParser parse) : base(parse)
        {
            FieldInput = parse.ParticleField("m_nFieldInput", FieldInput);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            OffetMin = parse.Vector3("m_vOffsetMin", OffetMin);
            OffsetMax = parse.Vector3("m_vOffsetMax", OffsetMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetVector(FieldInput);
            var output = particle.GetVector(FieldOutput);

            var offset = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, OffetMin, OffsetMax);

            particle.SetVector(FieldOutput, input + output + offset);

            return particle;
        }
    }
}
