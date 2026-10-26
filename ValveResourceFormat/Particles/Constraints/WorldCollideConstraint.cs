namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Keeps particles, as spheres of their radius, on the front side of the world surfaces found by
    /// tracing in 26 directions from control point 0. The first step traces every direction; each
    /// later step retraces one more slot, so the surfaces follow a moving control point gradually.
    /// Only <see cref="ParticleField.Position"/> is written, so a push also becomes velocity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_WorldCollideConstraint">C_OP_WorldCollideConstraint</seealso>
    class WorldCollideConstraint : ParticleFunctionConstraint
    {
        private CollisionPlaneSet planes = new();
        private float planesTime = float.NaN;

        public WorldCollideConstraint(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask();

        public override void Reset()
        {
            planes = new CollisionPlaneSet();
            planesTime = float.NaN;
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

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var radii = current.ReadFloats(ParticleField.Radius);

            for (var i = 0; i < positions.Length; i++)
            {
                if (planes.PushOut(ref positions[i], radii[i]))
                {
                    pushed = true;
                }
            }

            return pushed;
        }
    }
}
