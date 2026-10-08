namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Collides particles with the world, tracing each one's movement over the step and stopping it
    /// on what it would pass through, its radius resting on the surface. A hit can bounce the particle
    /// off the surface, let it slide along, stick it in place, or kill it.
    /// </summary>
    /// <remarks>
    /// Every collision mode is served by a per-particle trace of the step's movement, the most precise
    /// of them; the cached modes are cheaper approximations of the same contact.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_WorldTraceConstraint">C_OP_WorldTraceConstraint</seealso>
    class WorldTraceConstraint : ParticleFunctionConstraint
    {
        private const int CollisionModeDisabled = -1;

        private readonly int collisionMode = 3;
        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider bounceAmount = new LiteralNumberProvider(0f);
        private readonly INumberProvider slideAmount = new LiteralNumberProvider(0f);
        private readonly INumberProvider randomDirectionScale = new LiteralNumberProvider(0f);
        private readonly INumberProvider stopSpeed = new LiteralNumberProvider(-1f);
        private readonly bool decayBounce;
        private readonly bool killOnContact;
        private readonly float minimumKillSpeed = -1f;
        private readonly bool setNormal;

        public WorldTraceConstraint(ParticleDefinitionParser parse) : base(parse)
        {
            collisionMode = parse.Data.ContainsKey("m_nCollisionMode")
                ? (int)parse.Enum("m_nCollisionMode", ParticleCollisionMode.COLLISION_MODE_PER_PARTICLE_TRACE)
                : collisionMode;
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
            bounceAmount = parse.NumberProvider("m_flBounceAmount", bounceAmount);
            slideAmount = parse.NumberProvider("m_flSlideAmount", slideAmount);
            randomDirectionScale = parse.NumberProvider("m_flRandomDirScale", randomDirectionScale);
            stopSpeed = parse.NumberProvider("m_flStopSpeed", stopSpeed);
            decayBounce = parse.Boolean("m_bDecayBounce", decayBounce);
            killOnContact = parse.Boolean("m_bKillonContact", killOnContact);
            minimumKillSpeed = parse.Float("m_flMinSpeed", minimumKillSpeed);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState)
        {
            var collision = particleSystemState.Collision;

            if (collisionMode == CollisionModeDisabled || !collision.HasGeometry)
            {
                return false;
            }

            var moved = false;

            foreach (ref var particle in particles.Current)
            {
                var step = particle.Position - particle.PositionPrevious;
                var stepLength = step.Length();

                if (stepLength <= float.Epsilon)
                {
                    continue;
                }

                // The trace is pushed ahead by the radius so the particle stops with its edge on the surface
                var radius = particle.Radius * radiusScale.NextNumber(ref particle, particleSystemState);
                var direction = step / stepLength;

                if (!collision.TraceRay(particle.PositionPrevious, particle.Position + (direction * radius), out var hit))
                {
                    continue;
                }

                var speed = frameTime > 0f ? stepLength / frameTime : 0f;

                if (killOnContact && (minimumKillSpeed < 0f || speed >= minimumKillSpeed))
                {
                    particle.Kill();
                    continue;
                }

                var normal = hit.Normal;
                var resting = hit.Position + (normal * radius);

                // Split the step into its part into the surface and its part along it, then rebuild the
                // outgoing step: the normal part reflected by the bounce, the tangent part kept by the slide
                var into = Vector3.Dot(step, normal) * normal;
                var along = step - into;
                var bounce = bounceAmount.NextNumber(ref particle, particleSystemState);

                if (decayBounce)
                {
                    bounce *= 1f - particle.NormalizedAge;
                }

                var outgoing = (along * slideAmount.NextNumber(ref particle, particleSystemState)) - (into * bounce);

                var scatter = randomDirectionScale.NextNumber(ref particle, particleSystemState);

                if (scatter != 0f)
                {
                    var random = particleSystemState.Random.NextBetweenPerComponent(-Vector3.One, Vector3.One) * scatter * outgoing.Length();
                    outgoing += random - (Vector3.Dot(random, normal) * normal);
                }

                var stop = stopSpeed.NextNumber(ref particle, particleSystemState);

                if (stop >= 0f && frameTime > 0f && outgoing.Length() / frameTime <= stop)
                {
                    outgoing = Vector3.Zero;
                }

                particle.Position = resting;
                particle.PositionPrevious = resting - outgoing;

                if (setNormal)
                {
                    particle.SetVector(ParticleField.Normal, normal);
                }

                moved = true;
            }

            return moved;
        }
    }
}
