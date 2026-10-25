namespace ValveResourceFormat.Particles.Constraints
{
    /// <summary>
    /// Pushes particles out of spheres around the parent system's particles, treating both as spheres
    /// of their radius, scaled by <c>m_flParentRadiusScale</c> and <c>m_flRadiusScale</c>. Parent
    /// particles are taken in order, each seeing where the previous ones pushed. Only
    /// <see cref="ParticleField.Position"/> is written, so a push also becomes velocity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CollideWithParentParticles">C_OP_CollideWithParentParticles</seealso>
    class CollideWithParentParticles : ParticleFunctionConstraint
    {
        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Position.Mask();

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

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var radii = current.ReadFloats(ParticleField.Radius);
            var radiusScales = radiusScale.Fill(current, particleSystemState);

            var parentPositions = parentParticles.ReadVectors(ParticleField.Position);
            var parentRadii = parentParticles.ReadFloats(ParticleField.Radius);

            // A per-particle parent radius scale is evaluated over this system's particles, not the
            // parent's, and evaluated again for every block of four; parent j reads the value of
            // particle j. Parents beyond this system's particle count, which would read past the
            // evaluated values, take 1. Any other input gives every parent its collection value
            var parentRadiusScales = ParticleScratch.Floats(current.Length);
            var isPerParticle = parentRadiusScale.IsPerParticle;
            var scaleBeyondParticles = 1f;

            if (!isPerParticle && !current.IsEmpty)
            {
                scaleBeyondParticles = parentRadiusScale.NextNumber(particleSystemState);
                parentRadiusScales.Fill(scaleBeyondParticles);
            }

            foreach (var (offset, block) in current.Blocks())
            {
                if (isPerParticle)
                {
                    parentRadiusScale.Fill(current, particleSystemState, parentRadiusScales);
                }

                for (var i = offset; i < offset + block.Length; i++)
                {
                    var radius = radii[i] * radiusScales[i];

                    for (var j = 0; j < parentPositions.Length; j++)
                    {
                        var parentScale = j < parentRadiusScales.Length ? parentRadiusScales[j] : scaleBeyondParticles;
                        var reach = (parentRadii[j] * parentScale) + radius;
                        var delta = positions[i] - parentPositions[j];
                        var distanceSquared = delta.LengthSquared();

                        if (distanceSquared >= reach * reach)
                        {
                            continue;
                        }

                        changed = true;

                        if (distanceSquared > 0f)
                        {
                            positions[i] = (delta * (reach / MathF.Sqrt(distanceSquared))) + parentPositions[j];
                        }
                    }
                }
            }

            return changed;
        }
    }
}
