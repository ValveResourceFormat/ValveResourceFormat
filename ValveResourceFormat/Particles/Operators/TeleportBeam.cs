namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Lays the particles out as dashes along a ballistic arc, each one a segment from its previous
    /// position to its position, with the dashes travelling along the arc over time. Everything about
    /// the arc comes from control points: its start, launch velocity, (mode, duration, alpha), two
    /// colours, and (fade start, unused, speed scale). Dashes past the fade start take the second
    /// colour and fade out by the end of the arc.
    /// </summary>
    /// <remarks>
    /// The dashes keep their phase across restarts.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_TeleportBeam">C_OP_TeleportBeam</seealso>
    class TeleportBeam : ParticleFunctionOperator
    {
        private const int SingleSegmentMode = 2;

        private readonly int cpPosition;
        private readonly int cpVelocity = 1;
        private readonly int cpMisc = 2;
        private readonly int cpColor = 3;
        private readonly int cpInvalidColor = 4;
        private readonly int cpExtraArcData = 5;
        private readonly Vector3 gravity = new(0f, 0f, -800f);
        private readonly float arcMaxDuration = 3f;
        private readonly float segmentBreak = 0.025f;
        private readonly float arcSpeed = 0.2f;

        private float arcStartTime;

        public TeleportBeam(ParticleDefinitionParser parse) : base(parse)
        {
            cpPosition = parse.Int32("m_nCPPosition", cpPosition);
            cpVelocity = parse.Int32("m_nCPVelocity", cpVelocity);
            cpMisc = parse.Int32("m_nCPMisc", cpMisc);
            cpColor = parse.Int32("m_nCPColor", cpColor);
            cpInvalidColor = parse.Int32("m_nCPInvalidColor", cpInvalidColor);
            cpExtraArcData = parse.Int32("m_nCPExtraArcData", cpExtraArcData);
            gravity = parse.Vector3("m_vGravity", gravity);
            arcMaxDuration = parse.Float("m_flArcMaxDuration", arcMaxDuration);
            segmentBreak = parse.Float("m_flSegmentBreak", segmentBreak);
            arcSpeed = parse.Float("m_flArcSpeed", arcSpeed);
        }

        public override void Initialize(ParticleSystemState particleSystemState) => arcStartTime = particleSystemState.Age;

        /// <inheritdoc/>
        public override ulong StoredFields
            => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.Color.Mask() | ParticleField.Alpha.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var live = particles.Current;
            var alphas = live.Floats(ParticleField.Alpha);
            var misc = particleSystemState.GetControlPoint(cpMisc).Position;

            if (misc.Z <= 0f)
            {
                HideFrom(alphas, 0);
                return;
            }

            var positions = live.Vectors(ParticleField.Position);
            var previousPositions = live.Vectors(ParticleField.PositionPrevious);
            var colors = live.Vectors(ParticleField.Color);

            var start = particleSystemState.GetControlPoint(cpPosition).Position;
            var velocity = particleSystemState.GetControlPoint(cpVelocity).Position;
            var extra = particleSystemState.GetControlPoint(cpExtraArcData).Position;
            var color = particleSystemState.GetControlPoint(cpColor).Position;
            var invalidColor = particleSystemState.GetControlPoint(cpInvalidColor).Position;

            Vector3 PositionAt(float t) => (velocity * t) + (gravity * (t * 0.5f * t)) + start;

            var segment = arcMaxDuration / live.Length;
            var time = particleSystemState.Age;
            var offset = (time - arcStartTime) * arcSpeed * extra.Z;

            if (offset > segment + segmentBreak)
            {
                offset = 0f;
                arcStartTime = time;
            }

            if ((int)misc.X == SingleSegmentMode)
            {
                previousPositions[0] = PositionAt(0f);
                positions[0] = PositionAt(MathF.Min(misc.Y, segment));
                colors[0] = color;
                alphas[0] = misc.Z;
                HideFrom(alphas, 1);
                return;
            }

            var arcEnd = MathF.Min(arcMaxDuration, misc.Y);

            if (offset > segmentBreak)
            {
                offset -= segmentBreak + segment;
            }

            for (var i = 0; i < live.Length; i++)
            {
                previousPositions[i] = PositionAt(MathF.Max(0f, offset));
                positions[i] = PositionAt(MathF.Min(offset + segment, arcEnd));

                var fading = extra.X < offset;

                colors[i] = fading ? invalidColor : color;
                alphas[i] = fading ? MathUtils.RemapValClamped(offset, extra.X, misc.Y, misc.Z, 0f) : misc.Z;
                offset += segment + segmentBreak;

                if (offset >= arcEnd)
                {
                    HideFrom(alphas, i + 1);
                    return;
                }
            }
        }

        private static void HideFrom(Span<float> alphas, int first)
        {
            alphas[first..].Clear();
        }
    }
}
