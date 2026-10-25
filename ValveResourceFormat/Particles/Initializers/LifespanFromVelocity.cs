using ValveResourceFormat.Particles.Constraints;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets each new particle's lifetime to the time it takes, moving at its current velocity, to
    /// reach the world: a trace along the velocity from <c>m_flTraceOffset</c> to
    /// <c>m_flMaxTraceLength</c> ahead finds the hit, and the distance to it, scaled per axis by
    /// <c>m_vecComponentScale</c>, is divided by the speed. A trace that misses times the particle to
    /// the end of the trace. A particle that starts on the surface is culled, and one that is not
    /// moving keeps its lifetime.
    /// </summary>
    /// <remarks>
    /// Traces are cached for the whole system tree in a ring of up to <c>m_nMaxPlanes</c> entries,
    /// which <see cref="CreateFromPlaneCache"/> places particles on. A particle whose trace would start
    /// and end within <c>m_flTraceTolerance</c> of a cached one reuses that hit instead of tracing.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_LifespanFromVelocity">C_INIT_LifespanFromVelocity</seealso>
    class LifespanFromVelocity : ParticleFunctionInitializer
    {
        private static readonly Vector3 NoHit = new(float.MaxValue);

        private readonly Vector3 componentScale = Vector3.One;
        private readonly float traceOffset;
        private readonly float maxTraceLength = 1024f;
        private readonly float traceTolerance = 64f;
        private readonly float traceSpan;
        private readonly int maxPlaneSlot;

        public LifespanFromVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            componentScale = parse.Vector3("m_vecComponentScale", componentScale);
            traceOffset = parse.Float("m_flTraceOffset", traceOffset);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            traceTolerance = parse.Float("m_flTraceTolerance", traceTolerance);
            maxPlaneSlot = Math.Min(parse.Int32("m_nMaxPlanes", 16), CollisionPlaneSet.MaxPlanes) - 1;
            traceSpan = maxTraceLength - traceOffset;
        }

        public override ulong WrittenFields => ParticleField.LifeDuration.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var lifetimes = run.Floats(ParticleField.LifeDuration);
            var positions = run.ReadVectors(ParticleField.Position);
            var previousPositions = run.ReadVectors(ParticleField.PositionPrevious);
            var frameTime = particleSystemState.PreviousStep;
            var cache = particleSystemState.CollisionPlaneSets[0] ??= new CollisionPlaneSet();

            for (var i = 0; i < lifetimes.Length; i++)
            {
                if (lifetimes[i] < 0f || !TryGetTrace(positions[i], previousPositions[i], frameTime, out var start, out var end, out var speed))
                {
                    continue;
                }

                var point = NoHit;

                if (traceTolerance * traceTolerance < cache.FindNearestTrace(start, end, ref point))
                {
                    point = Trace(particleSystemState.Collision, start, end, out var normal);
                    cache.AddTrace(start, end, point, normal, maxPlaneSlot);
                }

                if (point == NoHit)
                {
                    lifetimes[i] = -1f;
                    continue;
                }

                lifetimes[i] = TimeToHit(start, point, speed);
            }
        }

        /// <summary>
        /// Initializes whole blocks of four. Each block looks its particles up in the cache as it stood
        /// before the block, then traces the ones no cached trace is close to in order. A particle the
        /// trace finds nothing for gets a lifetime of 0 rather than being culled.
        /// </summary>
        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var lifetimes = run.Floats(ParticleField.LifeDuration);
            var positions = run.ReadVectors(ParticleField.Position);
            var previousPositions = run.ReadVectors(ParticleField.PositionPrevious);
            var frameTime = particleSystemState.PreviousStep;
            var cache = particleSystemState.CollisionPlaneSets[0] ??= new CollisionPlaneSet();

            Span<Vector3> starts = stackalloc Vector3[4];
            Span<Vector3> ends = stackalloc Vector3[4];
            Span<Vector3> points = stackalloc Vector3[4];
            Span<float> speeds = stackalloc float[4];
            Span<bool> needsTrace = stackalloc bool[4];

            for (var blockStart = 0; blockStart < lifetimes.Length; blockStart += 4)
            {
                for (var lane = 0; lane < 4; lane++)
                {
                    var i = blockStart + lane;
                    speeds[lane] = 0f;
                    needsTrace[lane] = false;

                    // A particle that is not moving keeps its lifetime, as it does outside whole blocks,
                    // instead of tracing along a NaN direction
                    if (lifetimes[i] < 0f || !TryGetTrace(positions[i], previousPositions[i], frameTime, out starts[lane], out ends[lane], out speeds[lane]))
                    {
                        continue;
                    }

                    points[lane] = NoHit;
                    needsTrace[lane] = traceTolerance * traceTolerance < cache.FindNearestTrace(starts[lane], ends[lane], ref points[lane]);
                }

                for (var lane = 0; lane < 4; lane++)
                {
                    if (needsTrace[lane])
                    {
                        points[lane] = Trace(particleSystemState.Collision, starts[lane], ends[lane], out var normal);
                        cache.AddTrace(starts[lane], ends[lane], points[lane], normal, maxPlaneSlot);
                    }
                }

                for (var lane = 0; lane < 4; lane++)
                {
                    if (speeds[lane] == 0f)
                    {
                        continue;
                    }

                    var i = blockStart + lane;

                    if (points[lane] == NoHit)
                    {
                        lifetimes[i] = 0f;
                        continue;
                    }

                    lifetimes[i] = TimeToHit(starts[lane], points[lane], speeds[lane]);
                }
            }
        }

        /// <summary>
        /// The trace along the velocity a particle moved at over the previous step, or false when it is
        /// not moving.
        /// </summary>
        private bool TryGetTrace(Vector3 position, Vector3 previousPosition, float frameTime, out Vector3 start, out Vector3 end, out float speed)
        {
            var velocity = Vector3.Zero;

            if (frameTime > 0f)
            {
                velocity = (position - previousPosition) / frameTime;
            }

            speed = velocity.Length();

            if (speed == 0f)
            {
                start = Vector3.Zero;
                end = Vector3.Zero;
                return false;
            }

            var direction = velocity / speed;
            start = (direction * traceOffset) + position;
            end = (direction * traceSpan) + start;
            return true;
        }

        private float TimeToHit(Vector3 start, Vector3 point, float speed)
        {
            var distance = ((start - point) * componentScale).Length();
            return (distance + traceOffset) / (speed + ParticleMath.FloatEpsilon);
        }

        private Vector3 Trace(IParticleCollision collision, Vector3 start, Vector3 end, out Vector3 normal)
        {
            var surfaceFraction = 1f - (maxTraceLength / (traceSpan + ParticleMath.FloatEpsilon));

            collision.TraceRay(start, end, out var point, out normal, out var fraction);

            if (fraction == 1f)
            {
                normal = NoHit;
            }

            if (surfaceFraction >= fraction || traceSpan == 0f)
            {
                normal = NoHit;
                return NoHit;
            }

            return point;
        }
    }
}
