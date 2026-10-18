using System.Runtime.InteropServices;
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
        private Vector3 runOffset;

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

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            Initialize(MemoryMarshal.CreateSpan(ref particle, 1), particles, particleSystemState);
            return particle;
        }

        public override void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var (head, blocks, tail) = SplitIntoCalls(batch);

            InitializeCall(batch[head], particleSystemState);
            InitializeCall(batch[blocks], particleSystemState);
            InitializeCall(batch[tail], particleSystemState);
        }

        private void InitializeCall(Span<Particle> call, ParticleSystemState particleSystemState)
        {
            if (call.IsEmpty)
            {
                return;
            }

            BeginRun(ref call[0], particleSystemState);

            foreach (ref var particle in call)
            {
                var offset = perParticle
                    ? Repulse(particleSystemState.Collision, particle.Position, perParticleTraceLength ? particle.Radius : traceLength, out _, out _)
                    : runOffset;

                if (proportional)
                {
                    offset *= particle.Radius;
                }

                particle.Position += offset;

                if (translate)
                {
                    particle.PositionPrevious += offset;
                }
            }
        }

        private void BeginRun(ref Particle first, ParticleSystemState particleSystemState)
        {
            Axes.CopyTo(directions, 0);

            if (perParticle)
            {
                return;
            }

            var origin = particleSystemState.GetControlPoint(controlPoint).GetPositionAtTime(first.CreationTime);

            if (inherit)
            {
                var magnitude = particleSystemState.GetControlPoint(controlPoint + 1).GetPositionAtTime(first.CreationTime).X;
                runOffset = MathUtils.Lerp(outputMin, outputMax, magnitude) * origin;
                return;
            }

            runOffset = Repulse(particleSystemState.Collision, origin, traceLength, out var direction, out var blocked);

            if (childControlPoint == -1 || particleSystemState.Data == null)
            {
                return;
            }

            children.Clear();
            particleSystemState.Data.CollectChildStatesInGroup(childGroupId, children);

            foreach (var child in children)
            {
                child.GetControlPoint(childControlPoint).Position = direction * blocked;
                child.GetControlPoint(childControlPoint + 1).Position = new Vector3(blocked, 0f, 0f);
            }
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
