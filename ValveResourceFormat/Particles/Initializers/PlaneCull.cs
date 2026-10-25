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
            controlPoint = parse.ControlPoint("m_nControlPoint", controlPoint);
            distance = parse.CollectionNumberProvider("m_flDistance", distance);
            cullInside = parse.Boolean("m_bCullInside", cullInside);
        }

        public override ulong WrittenFields => ParticleField.LifeDuration.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var lifetimes = run.Floats(ParticleField.LifeDuration);
            var positions = run.ReadVectors(ParticleField.Position);
            var point = particleSystemState.GetControlPoint(controlPoint);
            var planeDistance = distance.NextNumber(particleSystemState);

            for (var i = 0; i < lifetimes.Length; i++)
            {
                if (lifetimes[i] == -1f)
                {
                    continue;
                }

                var signedDistance = Vector3.Dot(positions[i] - point.Position, point.Forward) - planeDistance;

                if ((cullInside ? -signedDistance : signedDistance) <= 0f)
                {
                    lifetimes[i] = -1f;
                }
            }
        }
    }
}
