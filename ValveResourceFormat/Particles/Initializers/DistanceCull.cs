namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Culls new particles that spawn outside a sphere around a control point, or inside it with
    /// <c>m_bCullInside</c>, by giving them a lifetime of -1. A particle on the sphere is culled either
    /// way, so the default distance of 0 culls every particle that is not culling inside.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_DistanceCull">C_INIT_DistanceCull</seealso>
    class DistanceCull : ParticleFunctionInitializer
    {
        private readonly int controlPoint;
        private readonly INumberProvider distance = new LiteralNumberProvider(0f);
        private readonly bool cullInside;

        public DistanceCull(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nControlPoint", controlPoint);
            distance = parse.CollectionNumberProvider("m_flDistance", distance);
            cullInside = parse.Boolean("m_bCullInside", cullInside);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.LifeDuration);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (particle.Lifetime == -1f)
            {
                return particle;
            }

            var radius = distance.NextNumber(particleSystemState);
            var distanceSquared = Vector3.DistanceSquared(particle.Position, particleSystemState.GetControlPoint(controlPoint).Position);

            if (cullInside ? distanceSquared <= radius * radius : radius * radius <= distanceSquared)
            {
                particle.Lifetime = -1f;
            }

            return particle;
        }
    }
}
