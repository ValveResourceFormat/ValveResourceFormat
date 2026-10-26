using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// When the system starts its endcap, maps the sequence, body part or mesh group every live
    /// particle holds in one attribute through lists of names of a model into another attribute,
    /// once. A restart does not arm it again.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapNamedModelSequenceEndCap">C_OP_RemapNamedModelSequenceEndCap</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapNamedModelBodyPartEndCap">C_OP_RemapNamedModelBodyPartEndCap</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapNamedModelMeshGroupEndCap">C_OP_RemapNamedModelMeshGroupEndCap</seealso>
    class RemapNamedModelElementEndCap : ParticleFunctionOperator
    {
        private readonly NamedModelElementRemap remap;
        private readonly ParticleField fieldInput = ParticleField.SequenceNumber;
        private readonly ParticleField fieldOutput = ParticleField.Radius;

        private bool done;

        public RemapNamedModelElementEndCap(ParticleDefinitionParser parse, ModelElementKind kind) : base(parse)
        {
            remap = new NamedModelElementRemap(parse, kind);
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            done = false;
            remap.Resolve(particleSystemState);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!particleSystemState.InEndCap || done)
            {
                return;
            }

            done = true;

            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var inputs = current.ReadFloats(fieldInput);
            var output = current.Floats(fieldOutput);

            for (var i = 0; i < output.Length; i++)
            {
                output[i] = remap.Remap((int)inputs[i], particleSystemState);
            }
        }
    }
}
