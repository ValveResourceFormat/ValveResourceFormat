namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes particles from a parent particle stream, mapping child particles to parent indices.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateFromParentParticles">C_INIT_CreateFromParentParticles</seealso>
    class CreateFromParentParticles : ParticleFunctionInitializer
    {
        private readonly float velocityScale = 1f;
        private readonly float increment = 1f;
        private readonly bool randomDistribution;
        private readonly int randomSeed;
        private readonly bool subFrame = true;
        private readonly bool setRopeSegmentID;

        public CreateFromParentParticles(ParticleDefinitionParser parse) : base(parse)
        {
            velocityScale = parse.Float("m_flVelocityScale", velocityScale);
            increment = parse.Float("m_flIncrement", increment);
            randomDistribution = parse.Boolean("m_bRandomDistribution", randomDistribution);
            randomSeed = parse.Int32("m_nRandomSeed", randomSeed);
            subFrame = parse.Boolean("m_bSubFrame", subFrame);
            setRopeSegmentID = parse.Boolean("m_bSetRopeSegmentID", setRopeSegmentID);
        }

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
                return particle;
            }

            var parentIndex = Math.Clamp(GetParentIndex(parentParticles.Length, particle.ParticleID), 0, parentParticles.Length - 1);
            particle.ParentParticleIndex = parentIndex;

            if (setRopeSegmentID)
            {
                particle.Sequence2 = parentIndex;
            }

            // The child spawns at the parent particle and inherits its velocity, derived from the parent's
            // Verlet step; the emit path encodes it back into Position/PositionPrevious after initializers.
            ref var parent = ref parentParticles[parentIndex];
            var parentStep = parent.Position - parent.PositionPrevious;
            var parentFrameTime = parentData.CurrentFrameTime;
            particle.Position = parent.Position;
            particle.Velocity = parentFrameTime > 0f
                ? (parentStep / parentFrameTime) * velocityScale
                : parent.Velocity * velocityScale;

            // Subframe interpolation is not supported yet in this renderer.
            _ = subFrame;

            return particle;
        }

        private int GetParentIndex(int parentCount, int particleId)
        {
            if (parentCount <= 0)
            {
                return 0;
            }

            if (randomDistribution)
            {
                var randomIndex = ParticleCollection.RandomBetween(particleId + randomSeed, 0, parentCount - 1);
                return Math.Clamp((int)MathF.Floor(randomIndex), 0, parentCount - 1);
            }

            // Walk the parent list by the raw (possibly fractional) increment; the running
            // index uses % which keeps the dividend's sign, so wrap explicitly.
            var index = (int)MathF.Floor(particleId * increment);
            return ((index % parentCount) + parentCount) % parentCount;
        }
    }
}
