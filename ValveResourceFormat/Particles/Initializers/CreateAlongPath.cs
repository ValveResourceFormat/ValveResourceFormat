using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles along a Bezier path defined by a sequence of control points, including the
    /// midpoint bulge and offsets, at a path parameter that is random unless <c>m_fT</c> says otherwise.
    /// Supports optional random CP pair selection, and can save the path parameter and control point
    /// pair to the hitbox offset for the lock-to-saved-path operators.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateAlongPath">C_INIT_CreateAlongPath</seealso>
    class CreateAlongPath : ParticleFunctionInitializer
    {
        private readonly INumberProvider maxDistance = new LiteralNumberProvider(0f);
        private readonly INumberProvider? pathParameter;
        private readonly bool useRandomCPs; // randomly select sequential CP pairs between start and end points
        private readonly bool saveOffset;
        private readonly ParticlePathParameters pathParams;

        public CreateAlongPath(ParticleDefinitionParser parse) : base(parse)
        {
            useRandomCPs = parse.Boolean("m_bUseRandomCPs", useRandomCPs);
            saveOffset = parse.Boolean("m_bSaveOffset", saveOffset);
            // Modern schema names it m_fMaxDistance; older content uses m_flMaxDistance.
            maxDistance = parse.NumberProvider("m_fMaxDistance", parse.NumberProvider("m_flMaxDistance", maxDistance));

            // Content without m_fT predates it and always drew a uniform random parameter.
            if (parse.Data.ContainsKey("m_fT"))
            {
                var block = parse.Data.GetSubCollection("m_fT");

                // The field defaults to a varying uniform draw over [0, 1), so a block that omits its type is one.
                pathParameter = block.IsCollection && !block.ContainsKey("m_nType")
                    ? new RandomNumberProvider(parse.Nested(block), defaultMax: 1f, defaultMode: ParticleFloatRandomMode.PF_RANDOM_MODE_VARYING)
                    : parse.NumberProvider("m_fT", new LiteralNumberProvider(0f));
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

            if (saveOffset)
            {
                particle.HitboxOffsetPosition = new Vector3(t, path.StartControlPointNumber, path.EndControlPointNumber);
            }

            return particle;
        }
    }
}
