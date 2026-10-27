using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Pushes new particles away from nearby world geometry. Traces along the six axes find how much
    /// of each direction is blocked; the offset points away from the blocked sides, scaled per axis
    /// between <c>m_vecOutputMin</c> and <c>m_vecOutputMax</c> by the total blocked amount, and is
    /// added to the position, and also to the previous position with <c>m_bTranslate</c> (otherwise
    /// it becomes velocity). The traces start at each particle, or once at the control point for the
    /// whole batch, which can also hand the result to a group of children as two control points.
    /// With <c>m_bInherit</c> the offset is built from the control point and the next one instead,
    /// with no traces.
    /// </summary>
    /// <remarks>
    /// Each trace direction is replaced by the push it produced, and the directions are only reset at
    /// the start of each run <see cref="ParticleFunctionInitializer.SplitIntoCalls"/> splits a batch
    /// into. Later particles of the same run trace along the pushes of the earlier ones.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitialRepulsionVelocity">C_INIT_InitialRepulsionVelocity</seealso>
    class InitialRepulsionVelocity : ParticleFunctionInitializer
    {
        private static readonly Vector3[] Axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

        private readonly Vector3 outputMin = Vector3.Zero;
        private readonly Vector3 outputMax = Vector3.One;
        private readonly int controlPoint;
        private readonly bool perParticle;
        private readonly bool translate;
        private readonly bool proportional;
        private readonly float traceLength = 64f;
        private readonly bool perParticleTraceLength;
        private readonly bool inherit;
        private readonly int childControlPoint = -1;
        private readonly int childGroupId;

        private readonly Vector3[] directions = new Vector3[Axes.Length];
        private readonly List<ParticleSystemState> children = [];

        public InitialRepulsionVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            outputMin = parse.Vector3("m_vecOutputMin", outputMin);
            outputMax = parse.Vector3("m_vecOutputMax", outputMax);
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            perParticle = parse.Boolean("m_bPerParticle", perParticle);
            translate = parse.Boolean("m_bTranslate", translate);
            proportional = parse.Boolean("m_bProportional", proportional);
            traceLength = parse.Float("m_flTraceLength", traceLength);
            perParticleTraceLength = parse.Boolean("m_bPerParticleTR", perParticleTraceLength);
            inherit = parse.Boolean("m_bInherit", inherit);
            childControlPoint = parse.Int32("m_nChildCP", childControlPoint);
            childGroupId = parse.Int32("m_nChildGroupID", childGroupId);
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var runOffset = BeginRun(run, particleSystemState);

            var positions = run.Vectors(ParticleField.Position);
            var previousPositions = translate ? run.Vectors(ParticleField.PositionPrevious) : default;
            var readsRadius = proportional || (perParticle && perParticleTraceLength);
            var radii = readsRadius ? run.ReadFloats(ParticleField.Radius) : default;

            for (var i = 0; i < positions.Length; i++)
            {
                var offset = runOffset;

                if (perParticle)
                {
                    var length = perParticleTraceLength ? radii[i] : traceLength;
                    offset = Repulse(particleSystemState.Collision, positions[i], length, out _, out _);
                }

                // Without per-particle traces, the game keeps the shared offset scaled by each radius for
                // the next particle of the call, compounding the radii
                if (proportional)
                {
                    offset *= radii[i];
                    runOffset = offset;
                }

                positions[i] += offset;

                if (translate)
                {
                    previousPositions[i] += offset;
                }
            }
        }

        /// <summary>
        /// Resets the trace directions and works out the offset every particle of the run shares, or
        /// zero when each particle traces for its own.
        /// </summary>
        private Vector3 BeginRun(ParticleSpan run, ParticleSystemState particleSystemState)
        {
            Axes.CopyTo(directions, 0);

            if (perParticle)
            {
                return Vector3.Zero;
            }

            var creationTime = run.ReadFloats(ParticleField.CreationTime)[0];
            var origin = particleSystemState.GetControlPoint(controlPoint).GetPositionAtTime(creationTime);

            if (inherit)
            {
                var magnitude = particleSystemState.GetControlPoint(controlPoint + 1).GetPositionAtTime(creationTime).X;
                return MathUtils.Lerp(outputMin, outputMax, magnitude) * origin;
            }

            var offset = Repulse(particleSystemState.Collision, origin, traceLength, out var direction, out var blocked);

            if (childControlPoint == -1 || particleSystemState.Data == null)
            {
                return offset;
            }

            children.Clear();
            particleSystemState.Data.CollectChildStatesInGroup(childGroupId, children);

            foreach (var child in children)
            {
                child.GetControlPoint(childControlPoint).Position = direction * blocked;
                child.GetControlPoint(childControlPoint + 1).Position = new Vector3(blocked, 0f, 0f);
            }

            return offset;
        }

        /// <summary>
        /// Traces from <paramref name="start"/> along the current directions and turns the blocked
        /// fractions into the offset away from the geometry, replacing each direction with its push.
        /// </summary>
        private Vector3 Repulse(IParticleCollision collision, Vector3 start, float length, out Vector3 direction, out float blocked)
        {
            var push = Vector3.Zero;
            blocked = 0f;

            for (var i = 0; i < directions.Length; i++)
            {
                var end = start + (directions[i] * length);
                var fraction = 1f;

                if (collision.HasGeometry && end != start)
                {
                    // The trace set and collision group are not parsed, so the trace hits all geometry
                    collision.TraceRay(start, end, out _, out _, out fraction);
                }

                var weight = 1f - fraction;
                directions[i] = -directions[i] * weight;
                push += directions[i];
                blocked += weight;
            }

            if (push == Vector3.Zero)
            {
                push = Vector3.UnitZ;
                blocked = 0f;
            }

            direction = Vector3.Normalize(push);

            return MathUtils.Lerp(outputMin, outputMax, blocked) * (direction * blocked);
        }
    }
}
