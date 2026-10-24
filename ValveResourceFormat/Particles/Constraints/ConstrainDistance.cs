namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Keeps every particle within a distance band around a center point, snapping any particle outside
    /// the band onto the nearer shell along its direction from the center. The center is an offset placed
    /// in the frame of the transform input (a control point), or the bare offset in world space when
    /// <c>m_bGlobalCenter</c> is set. A negative <c>m_fMaxDistance</c> disables the outer shell. Without a
    /// positive <c>m_fMinDistance</c> there is no inner shell, and a particle closer than the magnitude of a
    /// negative minimum is pushed out to the maximum instead. With an inner shell, a particle sitting exactly
    /// on the center is pushed out along a tiny random offset.
    ///
    /// <para>Particles are handled in blocks of four. In a block that pushes a particle off the center,
    /// every other particle's offset from the center becomes its X offset on all three axes before it
    /// is constrained, which also moves particles inside the band.</para>
    ///
    /// <para>Only <see cref="Particle.Position"/> is written; <see cref="Particle.PositionPrevious"/> is
    /// left where it was, so the correction shows up as velocity on the next movement step.</para>
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ConstrainDistance">C_OP_ConstrainDistance</seealso>
    class ConstrainDistance : ParticleFunctionConstraint
    {
        private const float CenterJitter = 0.001f;

        private readonly INumberProvider minDistance = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxDistance = new LiteralNumberProvider(100f);
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly IVectorProvider centerOffset = new LiteralVectorProvider(Vector3.Zero);
        private readonly bool globalCenter;

        public ConstrainDistance(ParticleDefinitionParser parse) : base(parse)
        {
            minDistance = parse.NumberProvider("m_fMinDistance", minDistance);
            maxDistance = parse.NumberProvider("m_fMaxDistance", maxDistance);
            transformInput = parse.TransformInput("m_nControlPointNumber", transformInput);
            centerOffset = parse.VectorProvider("m_CenterOffset", centerOffset);
            globalCenter = parse.Boolean("m_bGlobalCenter", globalCenter);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var offset = centerOffset.NextVector(particleSystemState);
            var center = globalCenter
                ? offset
                : Vector3.Transform(offset, transformInput.NextTransform(particleSystemState));

            var min = minDistance.NextNumber(particleSystemState);
            var max = maxDistance.NextNumber(particleSystemState);

            if (max < 0f)
            {
                max = float.MaxValue;
            }

            var hasMin = min > 0f;
            var minSquared = min * min;
            var maxSquared = max * max;

            var moved = false;
            var current = particles.Current;
            Span<Vector3> deltas = stackalloc Vector3[4];

            for (var blockStart = 0; blockStart < current.Length; blockStart += 4)
            {
                var block = current.Slice(blockStart, Math.Min(4, current.Length - blockStart));
                var anyOutside = false;
                var anyAtCenter = false;

                for (var lane = 0; lane < block.Length; lane++)
                {
                    deltas[lane] = block[lane].Position - center;
                    var distanceSquared = deltas[lane].LengthSquared();

                    anyOutside |= distanceSquared < minSquared || distanceSquared > maxSquared;
                    anyAtCenter |= distanceSquared <= 0f;
                }

                if (!anyOutside)
                {
                    continue;
                }

                var reseedCenter = hasMin && anyAtCenter;

                for (var lane = 0; lane < block.Length; lane++)
                {
                    var particle = block[lane];
                    var delta = deltas[lane];
                    var distanceSquared = delta.LengthSquared();
                    var outside = distanceSquared < minSquared || distanceSquared > maxSquared;
                    var target = hasMin && distanceSquared < minSquared ? min : max;

                    if (!outside && !reseedCenter)
                    {
                        continue;
                    }

                    if (reseedCenter)
                    {
                        delta = distanceSquared <= 0f
                            ? particleSystemState.Random.NextBetweenPerComponent(new Vector3(-CenterJitter), new Vector3(CenterJitter))
                            : new Vector3(delta.X);
                    }

                    var newDistanceSquared = delta.LengthSquared();

                    if (newDistanceSquared <= 0f)
                    {
                        continue;
                    }

                    var position = outside
                        ? center + (delta * (target / MathF.Sqrt(newDistanceSquared)))
                        : center + delta;

                    if (position != particle.Position)
                    {
                        particle.Position = position;
                        moved = true;
                    }
                }
            }

            return moved;
        }
    }
}
