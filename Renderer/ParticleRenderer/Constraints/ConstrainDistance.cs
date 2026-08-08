namespace ValveResourceFormat.Renderer.Particles.Constraints
{
    /// <summary>
    /// Keeps every particle within a distance band around a center point, snapping any particle outside
    /// the band onto the nearer shell along its direction from the center. The center is a control point
    /// plus an offset rotated into that control point's frame, or the bare offset in world space when
    /// <c>m_bGlobalCenter</c> is set. A negative <c>m_fMaxDistance</c> disables the outer shell, and a
    /// particle sitting exactly on the center has no direction to snap along so it is left where it is.
    ///
    /// <para>Only <see cref="Particle.Position"/> is written; <see cref="Particle.PositionPrevious"/> is
    /// left where it was, so the correction shows up as velocity on the next movement step.</para>
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ConstrainDistance">C_OP_ConstrainDistance</seealso>
    class ConstrainDistance : ParticleFunctionConstraint
    {
        private readonly INumberProvider minDistance = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxDistance = new LiteralNumberProvider(100f);
        private readonly int controlPoint;
        private readonly Vector3 centerOffset;
        private readonly bool globalCenter;

        public ConstrainDistance(ParticleDefinitionParser parse) : base(parse)
        {
            minDistance = parse.NumberProvider("m_fMinDistance", minDistance);
            maxDistance = parse.NumberProvider("m_fMaxDistance", maxDistance);
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            centerOffset = parse.Vector3("m_CenterOffset", centerOffset);
            globalCenter = parse.Boolean("m_bGlobalCenter", globalCenter);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var center = globalCenter
                ? centerOffset
                : ControlPointTransformProvider.TransformPosition(particleSystemState, controlPoint, centerOffset);

            var min = minDistance.NextNumber(particleSystemState);
            var max = maxDistance.NextNumber(particleSystemState);

            if (max < 0f)
            {
                max = float.MaxValue;
            }

            var minSquared = min * min;
            var maxSquared = max * max;

            var moved = false;

            foreach (ref var particle in particles.Current)
            {
                var delta = particle.Position - center;
                var distanceSquared = delta.LengthSquared();

                if (distanceSquared >= minSquared && distanceSquared <= maxSquared)
                {
                    continue;
                }

                if (distanceSquared == 0f)
                {
                    continue;
                }

                var target = distanceSquared < minSquared ? min : max;

                particle.Position = center + (delta * (target / MathF.Sqrt(distanceSquared)));
                moved = true;
            }

            return moved;
        }
    }
}
