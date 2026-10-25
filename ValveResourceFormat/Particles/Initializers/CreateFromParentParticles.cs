using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Initializes particles from a parent particle stream, mapping child particles to parent indices.
    /// Each new particle is bound to a parent and placed along the parent's step for this frame. The
    /// previous position is pulled toward the parent's by <c>m_flVelocityScale</c>, so 0 leaves the
    /// particle at rest and 1 keeps the parent's motion from the start of the step to the birth point.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateFromParentParticles">C_INIT_CreateFromParentParticles</seealso>
    class CreateFromParentParticles : ParticleFunctionInitializer
    {
        private readonly float velocityScale;
        private readonly float increment = 1f;
        private readonly bool randomDistribution;
        private readonly int randomSeed;
        private readonly bool subFrame = true;
        private readonly bool setRopeSegmentID;

        /// <summary>
        /// Whether the increment carries a fractional part. It selects both the index formula the
        /// random walk uses and whether a child blends between two neighbouring parents.
        /// </summary>
        private readonly bool fractionalIncrement;

        /// <summary>The parent particle attributes are only written from behavior version 9.</summary>
        private readonly bool writeParentIndex;

        private float currentParentIndex;
        private int randomCounter;

        public override void Reset()
        {
            currentParentIndex = 0f;
            randomCounter = 0;
        }

        public CreateFromParentParticles(ParticleDefinitionParser parse) : base(parse)
        {
            velocityScale = parse.Float("m_flVelocityScale", velocityScale);
            increment = parse.Float("m_flIncrement", increment);
            randomDistribution = parse.Boolean("m_bRandomDistribution", randomDistribution);
            randomSeed = parse.Int32("m_nRandomSeed", randomSeed);
            subFrame = parse.Boolean("m_bSubFrame", subFrame);
            setRopeSegmentID = parse.Boolean("m_bSetRopeSegmentID", setRopeSegmentID);
            writeParentIndex = parse.BehaviorVersion >= 9;

            fractionalIncrement = increment != MathF.Floor(increment);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.LifeDuration.Mask() | ParticleField.PositionPrevious.Mask()
            | ParticleField.ParentParticleIndex.Mask() | ParticleField.ParentParticleId.Mask()
            | (setRopeSegmentID ? ParticleField.RopeSegmentId.Mask() : 0);

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var positions = run.Vectors(ParticleField.Position);
            var previous = run.Vectors(ParticleField.PositionPrevious);

            var parentData = particleSystemState.ParentSystem?.Data;
            if (parentData == null)
            {
                positions.Clear();
                previous.Clear();
                return;
            }

            var parentParticles = parentData.CurrentParticles;
            if (parentParticles.Length == 0)
            {
                run.Floats(ParticleField.LifeDuration).Clear();
                positions.Clear();
                previous.Clear();
                return;
            }

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var parentIndices = writeParentIndex ? run.Integers(ParticleField.ParentParticleIndex) : default;
            var parentIds = writeParentIndex ? run.Integers(ParticleField.ParentParticleId) : default;
            var ropeSegmentIds = writeParentIndex && setRopeSegmentID ? run.Integers(ParticleField.RopeSegmentId) : default;
            var lastIndex = parentParticles.Length - 1;
            var parentParticleIds = parentParticles.ReadIntegers(ParticleField.ParticleId);
            var parentPositions = parentParticles.ReadVectors(ParticleField.Position);
            var parentPreviousPositions = parentParticles.ReadVectors(ParticleField.PositionPrevious);

            for (var i = 0; i < run.Length; i++)
            {
                var walkIndex = NextParentIndex(lastIndex, particleSystemState);
                var parentIndex = Math.Clamp((int)MathF.Floor(walkIndex), 0, lastIndex);

                if (writeParentIndex)
                {
                    parentIndices[i] = parentIndex;
                    parentIds[i] = parentParticleIds[parentIndex];

                    if (setRopeSegmentID)
                    {
                        ropeSegmentIds[i] = parentParticleIds[parentIndex];
                    }
                }

                var parentPosition = parentPositions[parentIndex];
                var parentPositionPrevious = parentPreviousPositions[parentIndex];

                if (fractionalIncrement)
                {
                    var nextIndex = Math.Clamp((int)MathF.Ceiling(walkIndex), 0, lastIndex);
                    var blend = MathUtils.Fract(walkIndex);
                    parentPosition = Vector3.Lerp(parentPosition, parentPositions[nextIndex], blend);
                    parentPositionPrevious = Vector3.Lerp(parentPositionPrevious, parentPreviousPositions[nextIndex], blend);
                }

                var birthFraction = subFrame ? GetBirthFraction(creationTimes[i], particleSystemState) : 1f;

                var position = MathUtils.Lerp(parentPositionPrevious, parentPosition, birthFraction);

                positions[i] = position;
                previous[i] = MathUtils.Lerp(position, parentPositionPrevious, velocityScale);
            }
        }

        /// <summary>
        /// Where inside the step being simulated the particle was born, as a fraction of it. A step of
        /// no duration counts as fully elapsed unless the particle is stamped in the past.
        /// </summary>
        private static float GetBirthFraction(float creationTime, ParticleSystemState particleSystemState)
        {
            var currentTime = particleSystemState.Age;
            var previousTime = currentTime - particleSystemState.CurrentStep;

            return MathUtils.RemapValClamped(creationTime, previousTime, currentTime, 0f, 1f);
        }

        /// <summary>
        /// Advances the walk over the parent list and returns the index the new particle binds to. A
        /// whole increment addresses one parent, a fractional one lands between two of them. The walk
        /// restarts at the first parent once it passes the last, rather than folding back with a modulo.
        /// A non-zero authored seed draws from a counter private to this initializer, leaving the
        /// system's shared draw sequence untouched.
        /// </summary>
        private float NextParentIndex(int lastIndex, ParticleSystemState particleSystemState)
        {
            if (randomDistribution)
            {
                var sample = particleSystemState.Random.NextOrSeeded(randomSeed, ref randomCounter);

                currentParentIndex = fractionalIncrement
                    ? sample * lastIndex
                    : (int)(sample * (lastIndex + 1));
            }
            else if (currentParentIndex > lastIndex)
            {
                currentParentIndex = 0f;
            }

            var index = currentParentIndex;
            currentParentIndex += increment;

            return index;
        }
    }
}
