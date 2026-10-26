using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Writes the index of a named sequence of a model into the second sequence number of new
    /// particles, 0 without a model and -1 when the model lacks it. Despite the name nothing is
    /// random. Looking the sequence up by activity is not supported and gives -1, and so does a name
    /// that matches no sequence but matches the activity of one.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomModelSequence">C_INIT_RandomModelSequence</seealso>
    class RandomModelSequence : ParticleFunctionInitializer
    {
        private readonly string sequenceName;
        private readonly ModelReference model;

        private int sequence = -1;

        public RandomModelSequence(ParticleDefinitionParser parse) : base(parse)
        {
            sequenceName = parse.String("m_SequenceName");
            model = new ModelReference(parse);
        }

        public override ulong WrittenFields => ParticleField.SecondSequenceNumber.Mask();

        public override void Initialize(ParticleSystemState particleSystemState)
        {
            sequence = -1;

            if (sequenceName.Length > 0)
            {
                sequence = model.Resolve(particleSystemState) is { } resolved
                    ? ModelElements.FindSequence(resolved, sequenceName, particleSystemState.Data?.FileLoader)
                    : 0;
            }
        }

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            run.Floats(ParticleField.SecondSequenceNumber).Fill(sequence);
        }
    }
}
