namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Culls new particles on one side of a plane through a control point, facing along its forward
    /// axis and pushed out by <c>m_flDistance</c>, by giving them a lifetime of -1. Particles at or
    /// behind the plane are culled, or those at or in front of it with <c>m_bCullInside</c>.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_PlaneCull">C_INIT_PlaneCull</seealso>
    class PlaneCull : ParticleFunctionInitializer
    {
        private readonly int controlPoint;
        private readonly INumberProvider distance = new LiteralNumberProvider(0f);
        private readonly bool cullInside;

        public PlaneCull(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = Math.Clamp(parse.Int32("m_nControlPoint", controlPoint), 0, ParticleSystemState.MaxControlPoint);
            distance = parse.CollectionNumberProvider("m_flDistance", distance);
            cullInside = parse.Boolean("m_bCullInside", cullInside);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.LifeDuration);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (particle.Lifetime == -1f)
            {
                return particle;
            }

            var point = particleSystemState.GetControlPoint(controlPoint);
            var signedDistance = Vector3.Dot(particle.Position - point.Position, point.Forward) - distance.NextNumber(particleSystemState);

            if ((cullInside ? -signedDistance : signedDistance) <= 0f)
            {
                particle.Lifetime = -1f;
            }

            return particle;
        }
    }
}
