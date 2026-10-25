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

        public override ulong WrittenFields => ParticleField.LifeDuration.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var lifetimes = run.Floats(ParticleField.LifeDuration);
            var positions = run.ReadVectors(ParticleField.Position);
            var radius = distance.NextNumber(particleSystemState);
            var controlPointPosition = particleSystemState.GetControlPoint(controlPoint).Position;

            for (var i = 0; i < run.Length; i++)
            {
                if (lifetimes[i] == -1f)
                {
                    continue;
                }

                var distanceSquared = Vector3.DistanceSquared(positions[i], controlPointPosition);

                if (cullInside ? distanceSquared <= radius * radius : radius * radius <= distanceSquared)
                {
                    lifetimes[i] = -1f;
                }
            }
        }
    }
}
