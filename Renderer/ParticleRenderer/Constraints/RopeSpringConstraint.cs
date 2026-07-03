namespace ValveResourceFormat.Renderer.Particles.Constraints
{
    /// <summary>
    /// Holds the distance between consecutive particles near a rest length, making a chain of particles
    /// behave like a rope. Combined with a gravity mover (<c>C_OP_BasicMovement</c>) and pinned endpoints
    /// (<see cref="ParticleField.ForceScale"/> 0), the chain settles under gravity into a catenary.
    ///
    /// <para>This is a <b>soft</b> spring, not a hard distance clamp: a segment outside the
    /// <c>[m_flMinDistance, m_flMaxDistance]</c> band (both multiplied by the rest length) is nudged toward
    /// the band by <c>frameTime * m_flAdjustmentScale</c> of the error each pass. The gentle correction lets
    /// the rope drape instead of snapping rigidly straight.</para>
    ///
    /// <para>The per-segment rest length is <c>baseLength * m_flRestLength</c>. <c>baseLength</c> is
    /// <c>m_flInitialRestingLength</c> when non-negative, otherwise the spawn chain length divided by the
    /// particle count (captured once). <c>m_flRestLength</c> is wired to the path_particle_rope
    /// <c>slack</c> (control point 1, Y) and is a bare multiplier: for slack &lt; 1 the
    /// rest length is shorter than the chord between the pinned nodes, so the rope is pulled taut and runs
    /// roughly straight along the path with only a slight gravity droop; slack &gt; 1 lets it sag.</para>
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RopeSpringConstraint">C_OP_RopeSpringConstraint</seealso>
    class RopeSpringConstraint : ParticleFunctionConstraint
    {
        private readonly INumberProvider restLength = new LiteralNumberProvider(1f);
        private readonly INumberProvider minDistance = new LiteralNumberProvider(0.9f);
        private readonly INumberProvider maxDistance = new LiteralNumberProvider(1.1f);
        private readonly INumberProvider initialRestingLength = new LiteralNumberProvider(-1f);
        private readonly float adjustmentScale = 15f;

        // Divisor floor for near-coincident segments, so the correction direction stays finite.
        private const float MinSegmentLength = 1.1920929e-07f;

        // The uniform per-segment base length, captured once from the spawn geometry.
        private bool captured;
        private float baseLength;

        public RopeSpringConstraint(ParticleDefinitionParser parse) : base(parse)
        {
            restLength = parse.NumberProvider("m_flRestLength", restLength);
            minDistance = parse.NumberProvider("m_flMinDistance", minDistance);
            maxDistance = parse.NumberProvider("m_flMaxDistance", maxDistance);
            initialRestingLength = parse.NumberProvider("m_flInitialRestingLength", initialRestingLength);
            adjustmentScale = parse.Float("m_flAdjustmentScale", adjustmentScale);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            var current = particles.Current;
            if (current.Length < 2)
            {
                return true;
            }

            // Rest segment length = spawn spacing * slack (a bare multiplier). slack < 1 holds the rope taut
            // and roughly straight along the path; slack > 1 lets gravity sag it into a catenary. Resolved on
            // the first pass before the large-step gate, so the rest length locks in from the spawn geometry
            // regardless of the timestep.
            var rest = ResolveBaseLength(current, particleSystemState) * restLength.NextNumber(particleSystemState);

            // Skip the solve on large steps: a step over 0.1s over-corrects the rope
            // instead of nudging it. The constraint is still reported as run.
            if (frameTime > 0.1f)
            {
                return true;
            }

            var min = minDistance.NextNumber(particleSystemState) * rest;
            var max = maxDistance.NextNumber(particleSystemState) * rest;

            // Soft spring: only a fraction (frameTime * adjustmentScale) of the length error is applied per
            // pass, over m_nMaxConstraintPasses passes per frame. frameTime is bounded by the dt <= 0.1 guard
            // above, so the correction rate stays gentle enough for gravity to bend the rope between passes.
            var scale = frameTime * adjustmentScale;

            for (var i = 1; i < current.Length; i++)
            {
                ref var a = ref current[i - 1];
                ref var b = ref current[i];

                var delta = b.Position - a.Position;
                var d = delta.Length();

                float target;
                if (d > max)
                {
                    target = max;
                }
                else if (d < min)
                {
                    target = min;
                }
                else
                {
                    continue;
                }

                // Move b toward / away from a along the connecting axis to reduce the length error, but only
                // by the soft fraction so gravity can still bend the rope between passes. The divisor is
                // clamped so near-coincident points still get a finite nudge.
                var correction = delta * (((target - d) / MathF.Max(d, MinSegmentLength)) * scale);

                var aPinned = a.ForceScale == 0f;
                var bPinned = b.ForceScale == 0f;

                if (bPinned)
                {
                    if (!aPinned)
                    {
                        a.Position -= correction;
                    }
                }
                else if (aPinned)
                {
                    b.Position += correction;
                }
                else
                {
                    a.Position -= 0.5f * correction;
                    b.Position += 0.5f * correction;
                }
            }

            return true;
        }

        /// <summary>
        /// Resolves the uniform per-segment base length. An explicit non-negative
        /// <c>m_flInitialRestingLength</c> is evaluated every frame; otherwise the base length is captured
        /// once from the spawn geometry (total chain length over particle count), while PositionPrevious still
        /// holds the spawn positions.
        /// </summary>
        private float ResolveBaseLength(Span<Particle> current, ParticleSystemRenderState particleSystemState)
        {
            var initial = initialRestingLength.NextNumber(particleSystemState);
            if (initial >= 0f)
            {
                return initial;
            }

            if (!captured)
            {
                var total = 0f;
                for (var i = 1; i < current.Length; i++)
                {
                    total += (current[i].PositionPrevious - current[i - 1].PositionPrevious).Length();
                }

                baseLength = total / current.Length;
                captured = true;
            }

            return baseLength;
        }
    }
}
