using System.Runtime.InteropServices;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Copies an attribute from the parent particles an event was raised on into the particles the
    /// event emits, cycling through the event's particles in order. Runs only for particles emitted
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

        public override ulong WrittenFields => CopiesAttribute ? FieldMask(attributeToCopy) : 0;

        public override bool ShouldRun(ParticleSystemState particleSystemState) => particleSystemState.EmitterEventParticles != null;

        public override void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var parentIndices = particleSystemState.EmitterEventParticles;

            if (!CopiesAttribute
                || (particleSystemState.InitializedFields & FieldMask(attributeToCopy)) != 0
                || parentIndices is not { Count: > 0 }
                || particleSystemState.ParentSystem?.Data is not { } parent)
            {
                return;
            }

            var parentParticles = parent.CurrentParticles;
            var isVector = attributeToCopy.FieldType() == "vector";
            var next = 0;

            foreach (ref var particle in batch)
            {
                ref var source = ref parentParticles[parentIndices[next]];
                next = next + 1 < parentIndices.Count ? next + 1 : 0;

                if (isVector)
                {
                    particle.SetVector(attributeToCopy, source.GetVector(attributeToCopy));
                }
                else
                {
                    particle.SetScalar(attributeToCopy, source.GetScalar(attributeToCopy));
                }
            }
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            Initialize(MemoryMarshal.CreateSpan(ref particle, 1), particles, particleSystemState);
            return particle;
        }
    }
}
