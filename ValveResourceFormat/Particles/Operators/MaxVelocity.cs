namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Clamps each particle's velocity to a maximum speed and raises it to a minimum speed, by moving the
    /// position relative to the previous position. The minimum wins when it exceeds the maximum, and a
    /// particle at rest is first given a small random step so it can be pushed up to the minimum.
    /// </summary>
    /// <remarks>
    /// Particles are handled in blocks of four. When a block holds a particle at rest and any particle
    /// in it has a minimum, every particle in the block also has its Z step replaced by
    /// <c>0.1 - x - y</c> of its own step.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MaxVelocity">C_OP_MaxVelocity</seealso>
    class MaxVelocity : ParticleFunctionOperator
    {
        private readonly INumberProvider maxVelocityProvider = new LiteralNumberProvider(0);
        private readonly INumberProvider minVelocityProvider = new LiteralNumberProvider(0);

        public MaxVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            maxVelocityProvider = parse.NumberProvider("m_flMaxVelocity", maxVelocityProvider);
            minVelocityProvider = parse.NumberProvider("m_flMinVelocity", minVelocityProvider);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            Span<Vector3> steps = stackalloc Vector3[4];

            var maxVelocities = maxVelocityProvider.Fill(current, particleSystemState);
            var minVelocities = minVelocityProvider.Fill(current, particleSystemState);

            foreach (var (offset, block) in current.Blocks())
            {
                var blockLength = block.Length;
                var anyMinimum = false;
                var anyAtRest = false;

                for (var lane = 0; lane < blockLength; lane++)
                {
                    var i = offset + lane;

                    steps[lane] = positions[i] - previousPositions[i];
                    anyMinimum |= minVelocities[i] > 0f;
                    anyAtRest |= steps[lane].LengthSquared() <= 0f;
                }

                if (anyMinimum && anyAtRest)
                {
                    var kicksX = particleSystemState.ThreadRandom.NextBetween(-0.1f, 0.1f);
                    var kicksY = particleSystemState.ThreadRandom.NextBetween(-0.1f, 0.1f);

                    for (var lane = 0; lane < blockLength; lane++)
                    {
                        var step = steps[lane];

                        if (step.LengthSquared() <= 0f)
                        {
                            step.X = kicksX[lane];
                            step.Y = kicksY[lane];
                        }

                        steps[lane] = step with { Z = 0.1f - step.X - step.Y };
                    }
                }

                for (var lane = 0; lane < blockLength; lane++)
                {
                    var i = offset + lane;
                    var step = steps[lane];
                    var length = step.Length();
                    var maxStep = maxVelocities[i] * frameTime;
                    var minStep = minVelocities[i] * frameTime;
                    var scale = 1f;

                    if (maxStep < length && length > 0f)
                    {
                        scale = maxStep / length;
                    }

                    if (length < minStep)
                    {
                        scale = minStep / length;
                    }

                    positions[i] = BlendByStrength(positions[i], (step * scale) + previousPositions[i], strength);
                }
            }
        }
    }
}
