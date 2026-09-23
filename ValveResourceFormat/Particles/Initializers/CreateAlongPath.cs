using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles along a Bezier path defined by a sequence of control points, including the
    /// midpoint bulge and offsets, at a path parameter that is random unless <c>m_fT</c> says otherwise.
    /// Supports optional random CP pair selection.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateAlongPath">C_INIT_CreateAlongPath</seealso>
    class CreateAlongPath : ParticleFunctionInitializer
    {
        private readonly INumberProvider maxDistance = new LiteralNumberProvider(0f);
        private readonly INumberProvider? pathParameter;
        private readonly bool useRandomCPs; // randomly select sequential CP pairs between start and end points
        private readonly ParticlePathParameters pathParams;

        public CreateAlongPath(ParticleDefinitionParser parse) : base(parse)
        {
            useRandomCPs = parse.Boolean("m_bUseRandomCPs", useRandomCPs);
            // Modern schema names it m_fMaxDistance; older content uses m_flMaxDistance.
            maxDistance = parse.NumberProvider("m_fMaxDistance", parse.NumberProvider("m_flMaxDistance", maxDistance));

            // Content without m_fT predates it and always drew a uniform random parameter.
            if (parse.Data.ContainsKey("m_fT"))
            {
                pathParameter = parse.NumberProvider("m_fT", new LiteralNumberProvider(0f));
            }

            pathParams = new ParticlePathParameters(parse);
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.HitboxOffsetPosition);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var path = pathParams;

            if (useRandomCPs)
            {
                var endCp = path.StartControlPointNumber + 1
                    + (int)(particleSystemState.Random.Next() * (path.EndControlPointNumber - path.StartControlPointNumber));
                path = path.WithControlPoints(endCp - 1, endCp);
            }

            var (start, mid, end) = ParticlePath.CalculatePathValues(particleSystemState, path, particle.CreationTime);

            var t = pathParameter?.NextNumber(ref particle, particleSystemState) ?? particleSystemState.Random.Next();
            var distance = maxDistance.NextNumber(ref particle, particleSystemState);

            var position = ParticlePath.Evaluate(start, mid, end, t);
            position += particleSystemState.Random.NextBetweenPerComponent(new Vector3(-distance), new Vector3(distance));

            particle.SetVector(ParticleField.Position, position);

            return particle;
        }
    }
}
