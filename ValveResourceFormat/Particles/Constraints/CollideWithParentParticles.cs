namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Pushes particles out of spheres around the parent system's particles, treating both as spheres
    /// of their radius, scaled by <c>m_flParentRadiusScale</c> and <c>m_flRadiusScale</c>. Parent
    /// particles are taken in order, each seeing where the previous ones pushed. Only
    /// <see cref="Particle.Position"/> is written, so a push also becomes velocity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CollideWithParentParticles">C_OP_CollideWithParentParticles</seealso>
    class CollideWithParentParticles : ParticleFunctionConstraint
    {
        private readonly INumberProvider parentRadiusScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider radiusScale = new LiteralNumberProvider(1f);

        public CollideWithParentParticles(ParticleDefinitionParser parse) : base(parse)
        {
            parentRadiusScale = parse.NumberProvider("m_flParentRadiusScale", parentRadiusScale);
            radiusScale = parse.NumberProvider("m_flRadiusScale", radiusScale);
        }

        public override bool ApplyConstraint(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var parentData = particleSystemState.ParentSystem?.Data;

            if (parentData == null)
            {
                return false;
            }

            var parentParticles = parentData.CurrentParticles;
            var changed = false;

            foreach (ref var particle in particles.Current)
            {
                var radius = particle.Radius * radiusScale.NextNumber(ref particle, particleSystemState);

                foreach (ref var parent in parentParticles)
                {
                    var reach = (parent.Radius * parentRadiusScale.NextNumber(ref parent, particleSystemState)) + radius;
                    var delta = particle.Position - parent.Position;
                    var distanceSquared = delta.LengthSquared();

                    if (distanceSquared >= reach * reach)
                    {
                        continue;
                    }

                    changed = true;

                    if (distanceSquared > 0f)
                    {
                        particle.Position = (delta * (reach / MathF.Sqrt(distanceSquared))) + parent.Position;
                    }
                }
            }

            return changed;
        }
    }
}
