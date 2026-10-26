namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Base class for all particle initializers. Initializers run once when a particle is created
    /// and set the particle's initial attribute values.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionInitializer">CParticleFunctionInitializer</seealso>
    abstract class ParticleFunctionInitializer : ParticleFunction
    {
        protected ParticleFunctionInitializer(ParticleDefinitionParser parse) : base(parse)
        {
            AssociatedEmitterIndex = parse.Int32("m_nAssociatedEmitterIndex", AssociatedEmitterIndex);
        }

        /// <summary>
        /// The position in the emitter list of the only emitter whose particles this initializer runs
        /// on, or -1 to run on every emitter's particles and the initial ones.
        /// </summary>
        public int AssociatedEmitterIndex { get; } = -1;

        /// <summary>
        /// Runs the initializer over the particles one emission created, which sit at the end of
        /// <paramref name="particles"/>, in the calls <see cref="SplitIntoCalls"/> gives, each with its
        /// inputs evaluated afresh.
        /// </summary>
        public void InitializeParticles(ParticleSpan batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var (head, blocks, tail) = SplitIntoCalls(batch);

            particleSystemState.InitializerBatchEnd = batch.Start + batch.Length;

            var headRun = batch[head];

            if (!headRun.IsEmpty)
            {
                BeginCall(headRun, particleSystemState);
                InitializeCall(headRun, particles, particleSystemState);
            }

            var blockRun = batch[blocks];

            if (!blockRun.IsEmpty)
            {
                BeginCall(blockRun, particleSystemState);
                InitializeBlocks(blockRun, particles, particleSystemState);
            }

            var tailRun = batch[tail];

            if (!tailRun.IsEmpty)
            {
                BeginCall(tailRun, particleSystemState);
                InitializeCall(tailRun, particles, particleSystemState);
            }

            particleSystemState.InitializerBatchEnd = 0;
            particleSystemState.InitializerRunEnd = 0;
        }

        /// <summary>Starts the call that initializes <paramref name="run"/>, whose inputs are evaluated afresh.</summary>
        private static void BeginCall(ParticleSpan run, ParticleSystemState particleSystemState)
        {
            particleSystemState.Random.BeginFunctionCall();
            particleSystemState.InitializerRunEnd = run.Start + run.Length;
        }

        /// <summary>
        /// Initializes the particles of <paramref name="run"/>, which is never empty, as a single call: a
        /// whole batch below 16 particles, or the particles before or after its whole blocks of four.
        /// </summary>
        protected abstract void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState);

        /// <summary>
        /// Initializes the whole blocks of four of a batch of 16 or more particles as a single call, which
        /// some initializers treat differently from the particles before and after them. Runs
        /// <see cref="InitializeCall"/> unless overridden.
        /// </summary>
        protected virtual void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeCall(run, particles, particleSystemState);
        }

        /// <summary>
        /// Splits a batch into the runs the particles are initialized in, one call each: the whole
        /// batch below 16 particles, else the particles before the first collection index that is a
        /// multiple of four, the whole blocks of four from there, and the rest. Any run can be empty;
        /// state an initializer keeps per call, such as its particle count, restarts with each run.
        /// </summary>
        private static (Range Head, Range Blocks, Range Tail) SplitIntoCalls(ParticleSpan batch)
        {
            if (batch.Length < 16)
            {
                return (Range.All, ^0.., ^0..);
            }

            var first = batch.Start;
            var blocksStart = MathUtils.AlignUp(first, 4) - first;
            var blocksEnd = ((first + batch.Length) & ~3) - first;

            return (..blocksStart, blocksStart..blocksEnd, blocksEnd..);
        }

        /// <summary>
        /// Whether the initializer takes part in initializing the particles being created now. One
        /// that does not is skipped as if disabled, and counts as writing nothing.
        /// </summary>
        public virtual bool ShouldRun(ParticleSystemState particleSystemState) => true;

        /// <summary>
        /// Whether <paramref name="field"/> is still unwritten for the particles being initialized:
        /// neither their creator nor an initializer before this one declared writing it.
        /// </summary>
        protected static bool IsUnwritten(ParticleSystemState particleSystemState, ParticleField field)
            => (particleSystemState.InitializedFields & field.Mask()) == 0;

        /// <summary>
        /// Gets the float <paramref name="field"/> of every particle in <paramref name="run"/> while it is
        /// still unwritten, or an empty span when a function before this one wrote it, so it is left alone.
        /// </summary>
        protected static Span<float> UnwrittenFloats(ParticleSpan run, ParticleSystemState particleSystemState, ParticleField field)
            => IsUnwritten(particleSystemState, field) ? run.Floats(field) : default;

        /// <summary>Gets the integer <paramref name="field"/> of every particle in <paramref name="run"/>, as <see cref="UnwrittenFloats"/> does.</summary>
        protected static Span<int> UnwrittenIntegers(ParticleSpan run, ParticleSystemState particleSystemState, ParticleField field)
            => IsUnwritten(particleSystemState, field) ? run.Integers(field) : default;

        /// <summary>Gets the vector <paramref name="field"/> of every particle in <paramref name="run"/>, as <see cref="UnwrittenFloats"/> does.</summary>
        protected static Span<Vector3> UnwrittenVectors(ParticleSpan run, ParticleSystemState particleSystemState, ParticleField field)
            => IsUnwritten(particleSystemState, field) ? run.Vectors(field) : default;

        /// <summary>
        /// Gets the previous positions a creator places along with the positions of <paramref name="run"/>,
        /// or an empty span when a function before this one wrote them, so they are left alone.
        /// </summary>
        protected static Span<Vector3> SpawnPreviousPositions(ParticleSpan run, ParticleSystemState particleSystemState)
            => UnwrittenVectors(run, particleSystemState, ParticleField.PositionPrevious);

        /// <summary>
        /// How far back to move a new particle's previous position to give it <paramref name="velocity"/>:
        /// one previous step of it, or <paramref name="velocity"/> itself when that is a displacement per step.
        /// </summary>
        protected static Vector3 VelocityStep(ParticleSystemState particleSystemState, Vector3 velocity, bool perStep)
            => perStep ? velocity : velocity * particleSystemState.PreviousStep;

        /// <summary>
        /// Bit mask over <see cref="ParticleField"/> values of the attributes this initializer
        /// declares it writes, not the ones it happens to assign. Under the pre-version-6
        /// first-writer-wins rule an initializer runs only while some attribute it declares is
        /// still unwritten, so one declaring nothing never runs there.
        /// </summary>
        public abstract ulong WrittenFields { get; }

        /// <inheritdoc/>
        /// <remarks>The attributes it declares in <see cref="WrittenFields"/>, unless overridden.</remarks>
        public override ulong StoredFields => WrittenFields;

        /// <summary>
        /// Position of this initializer in the definition's initializer list, counting entries the
        /// renderer dropped. The pre-version-6 first-writer-wins rule stops applying at
        /// <c>m_nFirstMultipleOverride_BackwardCompat</c>, which indexes this list.
        /// </summary>
        public int DefinitionIndex { get; set; } = -1;

        /// <summary>
        /// Whether the initializer declares a random float input, which counts as reading the particle
        /// ids and so advances the particle id counter, whether or not a call evaluates the input.
        /// </summary>
        public bool HasRandomInput { get; set; }
    }
}
