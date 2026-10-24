using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Culls new particles, or shortens their life, by tracing a ray from each against a ray trace
    /// environment, a set of map volumes such as the precipitation volumes or their blockers. The ray
    /// runs along <c>m_vecTestDir</c> without limit, or with <c>m_bUseVelocity</c> along the
    /// particle's velocity as far as it travels in its life. A hit, with the normal
    /// <c>m_vecTestNormal</c> when that is set, culls the particle, or with <c>m_bLifeAdjust</c> ends
    /// its life when it reaches the hit. <c>m_bCullOnMiss</c> culls on a miss instead. Where the host
    /// has no such environment, particles are left alone.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RtEnvCull">C_INIT_RtEnvCull</seealso>
    class RtEnvCull : ParticleFunctionInitializer
    {
        private const float Culled = -1f;

        private readonly Vector3 testDirection = Vector3.UnitZ;
        private readonly Vector3 testNormal = Vector3.Zero;
        private readonly bool useVelocity;
        private readonly bool cullOnMiss;
        private readonly bool lifeAdjust;
        private readonly string environmentName = "PRECIPITATION";
        private readonly int environmentControlPoint = -1;
        private readonly int component;

        private IParticleCollision? resolvedFor;
        private int environment = -1;

        public RtEnvCull(ParticleDefinitionParser parse) : base(parse)
        {
            testDirection = parse.Vector3("m_vecTestDir", testDirection);
            testNormal = parse.Vector3("m_vecTestNormal", testNormal);
            useVelocity = parse.Boolean("m_bUseVelocity", useVelocity);
            cullOnMiss = parse.Boolean("m_bCullOnMiss", cullOnMiss);
            lifeAdjust = parse.Boolean("m_bLifeAdjust", lifeAdjust);
            environmentName = parse.String("m_RtEnvName", environmentName);
            environmentControlPoint = parse.Int32("m_nRTEnvCP", environmentControlPoint);
            component = parse.Int32("m_nComponent", component);
        }

        public override void Reset()
        {
            resolvedFor = null;
        }

        public override ulong WrittenFields => FieldMask(ParticleField.LifeDuration);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var collision = particleSystemState.Collision;

            if (resolvedFor != collision)
            {
                resolvedFor = collision;
                environment = collision.FindRayTraceEnvironment(environmentName);
            }

            if (environmentControlPoint > -1)
            {
                environment = (int)particleSystemState.GetControlPoint(environmentControlPoint).Position.GetComponent(component);
            }

            if (environment < 0 || particle.Lifetime == Culled)
            {
                return particle;
            }

            var hasTestNormal = testNormal != Vector3.Zero;

            if (!useVelocity)
            {
                var found = collision.TraceEnvironment(environment, particle.Position, testDirection, float.MaxValue, out _, out var normal);
                var cull = found && (!hasTestNormal || normal == testNormal)
                    ? !cullOnMiss || hasTestNormal
                    : cullOnMiss;

                if (cull)
                {
                    particle.Lifetime = Culled;
                }

                return particle;
            }

            var step = particle.Position - particle.PositionPrevious;
            var stepLength = step.Length();
            var speed = stepLength / particles.PreviousFrameTime;
            var hit = false;
            var hitDistance = 0f;

            if (stepLength > 0f)
            {
                hit = collision.TraceEnvironment(environment, particle.Position, step / stepLength, particle.Lifetime * speed, out hitDistance, out var normal)
                    && (!hasTestNormal || normal == testNormal);
            }

            if (!hit)
            {
                if (cullOnMiss)
                {
                    particle.Lifetime = Culled;
                }
            }
            else if (lifeAdjust)
            {
                particle.Lifetime = MathF.Min(hitDistance / (speed + ParticleMath.FloatEpsilon), particle.Lifetime);
            }
            else if (!cullOnMiss)
            {
                particle.Lifetime = Culled;
            }

            return particle;
        }
    }
}
