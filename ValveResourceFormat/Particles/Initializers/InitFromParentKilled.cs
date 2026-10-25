namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Copies an attribute from the parent particles an event was raised on into the particles the
    /// event emits, cycling through the event's particles in order from the first in each call. Runs only for particles emitted
    /// from a parent's event, whichever event that is, and leaves the attribute alone when an earlier
    /// initializer or the emitter has already written it.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFromParentKilled">C_INIT_InitFromParentKilled</seealso>
    class InitFromParentKilled : ParticleFunctionInitializer
    {
        private readonly ParticleField attributeToCopy = (ParticleField)(-1);

        public InitFromParentKilled(ParticleDefinitionParser parse) : base(parse)
        {
            attributeToCopy = parse.ParticleField("m_nAttributeToCopy", attributeToCopy);
        }

        private bool CopiesAttribute => attributeToCopy >= 0 && attributeToCopy != ParticleField.NoneDisabled;

        public override ulong WrittenFields => CopiesAttribute ? attributeToCopy.Mask() : 0;

        public override bool ShouldRun(ParticleSystemState particleSystemState) => particleSystemState.EmitterEventParticles != null;

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var parentIndices = particleSystemState.EmitterEventParticles;

            if (!CopiesAttribute
                || !IsUnwritten(particleSystemState, attributeToCopy)
                || parentIndices is not { Count: > 0 }
                || particleSystemState.ParentSystem?.Data is not { } parent)
            {
                return;
            }

            var parentParticles = parent.CurrentParticles;
            var vectorOutput = ParticleBuffer.IsVector(attributeToCopy) ? run.Vectors(attributeToCopy) : default;
            var scalarOutput = ParticleBuffer.IsScalar(attributeToCopy) ? run.Floats(attributeToCopy) : default;
            var next = 0;

            var parentVectors = vectorOutput.IsEmpty ? default : parentParticles.ReadVectors(attributeToCopy);
            var parentScalars = scalarOutput.IsEmpty ? default : parentParticles.ReadFloats(attributeToCopy);

            for (var i = 0; i < run.Length; i++)
            {
                var source = parentIndices[next];
                next = next + 1 < parentIndices.Count ? next + 1 : 0;

                if (!vectorOutput.IsEmpty)
                {
                    vectorOutput[i] = parentVectors[source];
                }
                else if (!scalarOutput.IsEmpty)
                {
                    scalarOutput[i] = parentScalars[source];
                }
            }
        }
    }
}
