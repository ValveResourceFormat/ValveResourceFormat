namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Pushes the system's particles out of each other, treating each as a sphere of its radius
    /// scaled by <c>m_flRadiusScale</c>. Particles are taken in blocks of four, each pushed against
    /// every particle in turn, so the result depends on particle order. A block is only handled when
    /// one of its particles moved faster than <c>m_flMinimumSpeed</c> over the step, but then all four
    /// are pushed. Only <see cref="Particle.Position"/> is written, so a push also becomes velocity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CollideWithSelf">C_OP_CollideWithSelf</seealso>
    class CollideWithSelf : ParticleFunctionConstraint
    {
        private const int BlockSize = 4;

        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider minimumSpeed = new LiteralNumberProvider(1f);
        private readonly bool checkSpeed;

        private float[] radii = [];

        public CollideWithSelf(ParticleDefinitionParser parse) : base(parse)
        {
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            minimumSpeed = parse.NumberProvider("m_flMinimumSpeed", minimumSpeed);
            checkSpeed = minimumSpeed is not LiteralNumberProvider { Value: <= 0f };
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;

            if (radii.Length < current.Length)
            {
                radii = new float[particles.Capacity];
            }

            for (var i = 0; i < current.Length; i++)
            {
                radii[i] = radiusScale.NextNumber(ref current[i], particleSystemState) * current[i].Radius;
            }

            var changed = false;

            for (var blockStart = 0; blockStart < current.Length; blockStart += BlockSize)
            {
                var blockEnd = Math.Min(blockStart + BlockSize, current.Length);

                if (checkSpeed && !AnyFastEnough(current, blockStart, blockEnd, frameTime, particleSystemState))
                {
                    continue;
                }

                for (var j = 0; j < current.Length; j++)
                {
                    var other = current[j].Position;

                    for (var i = blockStart; i < blockEnd; i++)
                    {
                        ref var particle = ref current[i];
                        var reach = radii[j] + radii[i];
                        var delta = particle.Position - other;
                        var distanceSquared = delta.LengthSquared();

                        if (distanceSquared >= reach * reach)
                        {
                            continue;
                        }

                        changed = true;

                        if (distanceSquared > 0f)
                        {
                            particle.Position = (delta * (reach / MathF.Sqrt(distanceSquared))) + other;
                        }
                    }
                }
            }

            return changed;
        }

        private bool AnyFastEnough(Span<Particle> current, int blockStart, int blockEnd, float frameTime, ParticleSystemState particleSystemState)
        {
            for (var i = blockStart; i < blockEnd; i++)
            {
                ref var particle = ref current[i];
                var speed = minimumSpeed.NextNumber(ref particle, particleSystemState);

                if (speed * speed * frameTime < Vector3.DistanceSquared(particle.Position, particle.PositionPrevious))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
