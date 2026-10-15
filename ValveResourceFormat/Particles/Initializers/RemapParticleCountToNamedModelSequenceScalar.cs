using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// <see cref="RemapParticleCountToScalar"/> with its output range running between the indices
    /// of two named sequences of a model, so the count steps through the sequences in between.
    /// After a restart the authored output range applies instead.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapParticleCountToNamedModelSequenceScalar">C_INIT_RemapParticleCountToNamedModelSequenceScalar</seealso>
    class RemapParticleCountToNamedModelSequenceScalar : RemapParticleCountToScalar
    {
        private readonly ModelReference model;
        private readonly string[] names;

        public RemapParticleCountToNamedModelSequenceScalar(ParticleDefinitionParser parse) : base(parse)
        {
            model = new ModelReference(parse);
            names = [parse.String("m_outputMinName"), parse.String("m_outputMaxName")];
        }

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            base.Initialize(particleSystemState);

            var indices = ModelElements.Resolve(ModelElementKind.Sequence, names, model.Resolve(particleSystemState), particleSystemState.Data?.FileLoader);
            OutputRange = (indices[0], indices[1]);
        }
    }
}
