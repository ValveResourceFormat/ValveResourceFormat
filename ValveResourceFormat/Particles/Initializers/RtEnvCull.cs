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

        public override ulong WrittenFields => ParticleField.LifeDuration.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particles, particleSystemState, blockRun: false);
        }

        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particles, particleSystemState, blockRun: true);
        }

        private void InitializeRun(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState, bool blockRun)
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

            // Without an environment there is nothing to trace against, so particles are kept even with m_bCullOnMiss
            if (environment < 0)
            {
                return;
            }

            var lifetimes = run.Floats(ParticleField.LifeDuration);
            var positions = run.ReadVectors(ParticleField.Position);
            var previousPositions = run.ReadVectors(ParticleField.PositionPrevious);
            var previousFrameTime = particleSystemState.PreviousStep;
            var hasTestNormal = testNormal != Vector3.Zero;

            for (var i = 0; i < lifetimes.Length; i++)
            {
                var lifetime = lifetimes[i];

                if (lifetime == Culled)
                {
                    continue;
                }

                var position = positions[i];

                if (blockRun)
                {
                    lifetimes[i] = CullInBlock(collision, position, previousPositions[i], lifetime, previousFrameTime, hasTestNormal);
                    continue;
                }

                if (!useVelocity)
                {
                    var found = collision.TraceEnvironment(environment, position, testDirection, float.MaxValue, out _, out var normal);
                    var cull = found && (!hasTestNormal || normal == testNormal)
                        ? !cullOnMiss || hasTestNormal
                        : cullOnMiss;

                    if (cull)
                    {
                        lifetimes[i] = Culled;
                    }

                    continue;
                }

                var step = position - previousPositions[i];
                var stepLength = step.Length();
                var speed = stepLength / previousFrameTime;
                var hit = false;
                var hitDistance = 0f;

                if (stepLength > 0f)
                {
                    hit = collision.TraceEnvironment(environment, position, step / stepLength, lifetime * speed, out hitDistance, out var normal)
                        && (!hasTestNormal || normal == testNormal);
                }

                if (!hit)
                {
                    if (cullOnMiss)
                    {
                        lifetimes[i] = Culled;
                    }
                }
                else if (lifeAdjust)
                {
                    lifetimes[i] = MathF.Min(hitDistance / (speed + ParticleMath.FloatEpsilon), lifetime);
                }
                else if (!cullOnMiss)
                {
                    lifetimes[i] = Culled;
                }
            }
        }

        /// <summary>
        /// The lifetime a particle in the whole blocks of four of a batch is left with. These decide by a
        /// rule of their own: with <c>m_bCullOnMiss</c> a miss, or a hit with the test normal when one is
        /// set, ends the particle; otherwise a hit with the test normal, or any hit when none is set. With
        /// the velocity, an ended particle is culled, or with <c>m_bLifeAdjust</c> lives until it reaches
        /// the hit, and a miss never shortens its life.
        /// </summary>
        private float CullInBlock(IParticleCollision collision, Vector3 position, Vector3 previousPosition, float lifetime, float previousFrameTime, bool hasTestNormal)
        {
            var hit = false;
            var hitDistance = 0f;
            var normal = Vector3.Zero;
            var speed = 0f;

            if (!useVelocity)
            {
                hit = collision.TraceEnvironment(environment, position, testDirection, float.MaxValue, out hitDistance, out normal);
            }
            else
            {
                var step = position - previousPosition;
                var stepLength = step.Length();
                speed = stepLength / previousFrameTime;

                if (stepLength > 0f)
                {
                    hit = collision.TraceEnvironment(environment, position, step / stepLength, lifetime * speed, out hitDistance, out normal);
                }
            }

            var matchesNormal = normal == testNormal;
            var ends = cullOnMiss
                ? !hit || (hasTestNormal && matchesNormal)
                : hit && (!hasTestNormal || matchesNormal);

            if (!ends)
            {
                return lifetime;
            }

            if (!useVelocity)
            {
                return Culled;
            }

            if (!lifeAdjust)
            {
                return MathF.Min(lifetime, Culled);
            }

            // A missed ray also divides the trace's undefined distance here; a miss keeps its life instead
            var lifeToHit = hit ? hitDistance / (speed + ParticleMath.FloatEpsilon) : float.PositiveInfinity;
            return MathF.Min(lifetime, lifeToHit);
        }
    }
}
