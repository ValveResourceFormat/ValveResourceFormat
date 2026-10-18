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
        private readonly int maxPlaneSlot;

        public LifespanFromVelocity(ParticleDefinitionParser parse) : base(parse)
        {
            componentScale = parse.Vector3("m_vecComponentScale", componentScale);
            traceOffset = parse.Float("m_flTraceOffset", traceOffset);
            maxTraceLength = parse.Float("m_flMaxTraceLength", maxTraceLength);
            traceTolerance = parse.Float("m_flTraceTolerance", traceTolerance);
            maxPlaneSlot = Math.Min(parse.Int32("m_nMaxPlanes", 16), CollisionPlaneSet.MaxPlanes) - 1;
        }

        public override ulong WrittenFields => FieldMask(ParticleField.LifeDuration);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (particle.Lifetime < 0f)
            {
                return particle;
            }

            var velocity = particle.GetVelocity(particles.PreviousFrameTime);
            var speed = velocity.Length();

            if (speed == 0f)
            {
                return particle;
            }

            var direction = velocity / speed;
            var traceSpan = maxTraceLength - traceOffset;
            var start = (direction * traceOffset) + particle.Position;
            var end = (direction * traceSpan) + start;

            var cache = particleSystemState.CollisionPlaneSets[0] ??= new CollisionPlaneSet();
            var point = NoHit;

            if (traceTolerance * traceTolerance < cache.FindNearestTrace(start, end, ref point))
            {
                point = Trace(particleSystemState.Collision, start, end, traceSpan, out var normal);
                cache.AddTrace(start, end, point, normal, maxPlaneSlot);
            }

            if (point == NoHit)
            {
                particle.Lifetime = -1f;
                return particle;
            }

            var distance = ((start - point) * componentScale).Length();
            particle.Lifetime = (distance + traceOffset) / (speed + ParticleMath.FloatEpsilon);

            return particle;
        }

        private Vector3 Trace(IParticleCollision collision, Vector3 start, Vector3 end, float traceSpan, out Vector3 normal)
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
