namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills particles that cross a spherical boundary around a control point, culling either inside or outside the sphere.
    /// </summary>
    /// <remarks>
    /// The step is skipped while the system's last bounds touch the sphere (or, culling inside, while
    /// they miss it), tested against the control point without <c>m_vecPointOffset</c>. Culling
    /// outside therefore keeps particles that have left the sphere for as long as any part of the
    /// bounds still reaches it.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_DistanceCull">C_OP_DistanceCull</seealso>
    class DistanceCull : ParticleFunctionOperator
    {
        private readonly int cp;
        private readonly INumberProvider distance = new LiteralNumberProvider(0);
        private readonly Vector3 pointOffset = Vector3.Zero;
        private readonly bool cullInside;
        private readonly ParticleField attribute = ParticleField.Position;

        public DistanceCull(ParticleDefinitionParser parse) : base(parse)
        {
            cp = parse.Int32("m_nControlPoint", cp);
            pointOffset = parse.Vector3("m_vecPointOffset", pointOffset);
            distance = parse.NumberProvider("m_flDistance", distance);
            cullInside = parse.Boolean("m_bCullInside", cullInside);
            attribute = parse.ParticleField("m_nAttribute", attribute);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var radius = distance.NextNumber(particleSystemState);
            var radiusSquared = radius * radius;
            var center = particleSystemState.GetControlPoint(cp).Position;

            if (particleSystemState.Data?.WorldBoundingBox is { } bounds)
            {
                var nearest = Vector3.Clamp(center, bounds.Min, bounds.Max);

                if ((Vector3.DistanceSquared(center, nearest) <= radiusSquared) != cullInside)
                {
                    return;
                }
            }

            var sphereOrigin = center + pointOffset;

            foreach (var particle in particles.Current)
            {
                var distanceSquared = Vector3.DistanceSquared(particle.GetVector(attribute), sphereOrigin);

                if (cullInside ? distanceSquared < radiusSquared : radiusSquared < distanceSquared)
                {
                    KillCulled(particle, particles, particleSystemState);
                }
            }
        }
    }
}
