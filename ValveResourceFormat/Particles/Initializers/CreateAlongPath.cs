using ValveKeyValue;
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
        private readonly INumberProvider pathParameter;
        private readonly bool useRandomCPs; // randomly select sequential CP pairs between start and end points
        private readonly bool saveOffset;
        private readonly ParticlePathParameters pathParams;

        public CreateAlongPath(ParticleDefinitionParser parse) : base(parse)
        {
            useRandomCPs = parse.Boolean("m_bUseRandomCPs", useRandomCPs);
            saveOffset = parse.Boolean("m_bSaveOffset", saveOffset);
            // Modern schema names it m_fMaxDistance; older content uses m_flMaxDistance.
            maxDistance = parse.NumberProvider("m_fMaxDistance", parse.NumberProvider("m_flMaxDistance", maxDistance));

            // The field defaults to a varying uniform draw over [0, 1), one per block of four particles, so a block that
            // omits its type is one too.
            var block = parse.Data.ContainsKey("m_fT") ? parse.Data.GetSubCollection("m_fT") : KVObject.ListCollection();

            pathParameter = block.IsCollection && !block.ContainsKey("m_nType")
                ? new RandomNumberProvider(parse.Nested(block), defaultMode: ParticleFloatRandomMode.PF_RANDOM_MODE_VARYING)
                : parse.NumberProvider("m_fT", new LiteralNumberProvider(0f));

            pathParams = new ParticlePathParameters(parse).WithClampedControlPoints();
        }

        public override ulong WrittenFields => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.HitboxOffsetPosition.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            // The inputs are evaluated for every live particle, and each particle of the run reads its own value
            var pathParameters = pathParameter.Fill(run, particles.Current, particleSystemState);
            var distances = maxDistance.Fill(run, particles.Current, particleSystemState);

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var positions = run.Vectors(ParticleField.Position);
            var previous = SpawnPreviousPositions(run, particleSystemState);
            var hitboxOffsets = saveOffset ? run.Vectors(ParticleField.HitboxOffsetPosition) : default;

            for (var i = 0; i < run.Length; i++)
            {
                var path = pathParams;

                if (useRandomCPs)
                {
                    var endCp = path.StartControlPointNumber + 1
                        + (int)(particleSystemState.Random.Next() * (path.EndControlPointNumber - path.StartControlPointNumber));
                    path = path.WithControlPoints(endCp - 1, endCp);
                }

                var (start, mid, end) = ParticlePath.CalculatePathValues(particleSystemState, path, creationTimes[i]);

                var t = pathParameters[i];
                var distance = distances[i];

                var position = ParticlePath.Evaluate(start, mid, end, t);
                position += particleSystemState.Random.NextBetweenPerComponent(new Vector3(-distance), new Vector3(distance));

                positions[i] = position;

                if (!previous.IsEmpty)
                {
                    previous[i] = position;
                }

                if (saveOffset)
                {
                    hitboxOffsets[i] = new Vector3(t, path.StartControlPointNumber, path.EndControlPointNumber);
                }
            }
        }
    }
}
