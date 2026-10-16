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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            Span<float> maxSteps = stackalloc float[4];
            Span<float> minSteps = stackalloc float[4];
            Span<Vector3> steps = stackalloc Vector3[4];

            for (var blockStart = 0; blockStart < current.Length; blockStart += 4)
            {
                var block = current.Slice(blockStart, Math.Min(4, current.Length - blockStart));
                var anyMinimum = false;
                var anyAtRest = false;

                for (var lane = 0; lane < block.Length; lane++)
                {
                    ref var particle = ref block[lane];
                    var maxVelocity = maxVelocityProvider.NextNumber(ref particle, particleSystemState);
                    var minVelocity = minVelocityProvider.NextNumber(ref particle, particleSystemState);

                    maxSteps[lane] = maxVelocity * frameTime;
                    minSteps[lane] = minVelocity * frameTime;
                    steps[lane] = particle.Position - particle.PositionPrevious;
                    anyMinimum |= minVelocity > 0f;
                    anyAtRest |= steps[lane].LengthSquared() <= 0f;
                }

                if (anyMinimum && anyAtRest)
                {
                    for (var lane = 0; lane < block.Length; lane++)
                    {
                        var step = steps[lane];

                        if (step.LengthSquared() <= 0f)
                        {
                            step.X = particleSystemState.Random.NextBetween(-0.1f, 0.1f);
                            step.Y = particleSystemState.Random.NextBetween(-0.1f, 0.1f);
                        }

                        steps[lane] = step with { Z = 0.1f - step.X - step.Y };
                    }
                }

                for (var lane = 0; lane < block.Length; lane++)
                {
                    ref var particle = ref block[lane];
                    var step = steps[lane];
                    var length = step.Length();
                    var scale = 1f;

                    if (maxSteps[lane] < length && length > 0f)
                    {
                        scale = maxSteps[lane] / length;
                    }

                    if (length < minSteps[lane])
                    {
                        scale = minSteps[lane] / length;
                    }

                    var clamped = (step * scale) + particle.PositionPrevious;

                    particle.Position = strength >= 1f ? clamped : (clamped * strength) + (particle.Position * (1f - strength));
                }
            }
        }
    }
}
