namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Keeps particles, as spheres of their radius, on the front side of the world surfaces found by
    /// tracing in 26 directions from control point 0. The first step traces every direction; each
    /// later step retraces one more slot, so the surfaces follow a moving control point gradually.
    /// Only <see cref="Particle.Position"/> is written, so a push also becomes velocity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_WorldCollideConstraint">C_OP_WorldCollideConstraint</seealso>
    class WorldCollideConstraint : ParticleFunctionConstraint
    {
        private CollisionPlaneSet planes = new();
        private float planesTime = float.NaN;

        public WorldCollideConstraint(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Reset()
        {
            planes = new CollisionPlaneSet();
            planesTime = float.NaN;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var collision = particleSystemState.Collision;

            if (!collision.HasGeometry)
            {
                return false;
            }

            var time = particleSystemState.Age;

            if (planesTime != time)
            {
                planesTime = time;
                planes.Refresh(collision, ParticleCollisionMode.COLLISION_MODE_PER_FRAME_PLANESET, particleSystemState.GetControlPoint(0).Position, 0f, force: true, time);
            }

            var pushed = false;

            foreach (var particle in particles.Current)
            {
                var position = particle.Position;

                if (planes.PushOut(ref position, particle.Radius))
                {
                    particle.Position = position;
                    pushed = true;
                }
            }

            return pushed;
        }
    }
}
