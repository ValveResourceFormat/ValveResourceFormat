using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes particles from a parent particle stream, mapping child particles to parent indices.
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

        /// <summary>The parent particle index attribute is only written from behavior version 9.</summary>
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

        /// <summary>
        /// Binds the new particle to a parent and places it along the parent's step for this frame,
        /// keeping the share of the parent's velocity left once the particle is born. The emit path
        /// encodes that velocity back into Position/PositionPrevious after initializers run.
        /// </summary>
        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var parentData = particleSystemState.ParentSystem?.Data;
            if (parentData == null)
            {
                return particle;
            }

            var parentParticles = parentData.CurrentParticles;
            if (parentParticles.Length == 0)
            {
                // A child with no parent to bind to is born dead rather than left at the origin
                particle.Lifetime = 0f;
                return particle;
            }

            var lastIndex = parentParticles.Length - 1;
            var walkIndex = NextParentIndex(lastIndex, particleSystemState);
            var parentIndex = Math.Clamp((int)MathF.Floor(walkIndex), 0, lastIndex);

            if (writeParentIndex)
            {
                particle.ParentParticleIndex = parentIndex;
            }

            ref var parent = ref parentParticles[parentIndex];

            particle.ParentParticleId = parent.ParticleId;

            if (setRopeSegmentID)
            {
                particle.RopeSegmentId = parent.ParticleId;
            }

            var parentPosition = parent.Position;
            var parentPositionPrevious = parent.PositionPrevious;

            if (fractionalIncrement)
            {
                ref var nextParent = ref parentParticles[Math.Clamp((int)MathF.Ceiling(walkIndex), 0, lastIndex)];
                var blend = walkIndex - MathF.Floor(walkIndex);
                parentPosition = Vector3.Lerp(parentPosition, nextParent.Position, blend);
                parentPositionPrevious = Vector3.Lerp(parentPositionPrevious, nextParent.PositionPrevious, blend);
            }

            var birthFraction = subFrame ? GetBirthFraction(particle, particles, particleSystemState) : 1f;

            particle.Position = Vector3.Lerp(parentPositionPrevious, parentPosition, birthFraction);

            var parentStep = parentPosition - parentPositionPrevious;
            var parentFrameTime = parentData.CurrentFrameTime;
            var parentVelocity = parentFrameTime > 0f
                ? parentStep / parentFrameTime
                : parent.Velocity;

            particle.Velocity = parentVelocity * birthFraction * velocityScale;

            return particle;
        }

        /// <summary>
        /// Where inside the step being simulated the particle was born, as a fraction of it. A step of
        /// no duration counts as fully elapsed unless the particle is stamped in the past.
        /// </summary>
        private static float GetBirthFraction(in Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var currentTime = particleSystemState.Age;
            var previousTime = currentTime - particles.CurrentFrameTime;

            if (previousTime == currentTime)
            {
                return particle.CreationTime >= currentTime ? 1f : 0f;
            }

            return Math.Clamp((particle.CreationTime - previousTime) / (currentTime - previousTime), 0f, 1f);
        }

        /// <summary>
        /// Advances the walk over the parent list and returns the index the new particle binds to. A
        /// whole increment addresses one parent, a fractional one lands between two of them. The walk
        /// restarts at the first parent once it passes the last, rather than folding back with a modulo.
        /// A non-zero authored seed draws from a counter private to this initializer, leaving the
        /// system's shared draw sequence untouched.
        /// </summary>
        private float NextParentIndex(int lastIndex, ParticleSystemRenderState particleSystemState)
        {
            if (randomDistribution)
            {
                var sample = randomSeed != 0
                    ? ParticleRandom.ForSample(particleSystemState.Random.Seed + randomSeed + randomCounter++)
                    : particleSystemState.Random.Next();

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
