using ValveResourceFormat.Renderer.Particles.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places particles at random parameters along a Bezier path defined by a sequence of control
    /// points, including the midpoint bulge and offsets. Supports optional random CP pair selection.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateAlongPath">C_INIT_CreateAlongPath</seealso>
    class CreateAlongPath : ParticleFunctionInitializer
    {
        private readonly float maxDistance;
        private readonly bool useRandomCPs; // randomly select sequential CP pairs between start and end points
        private readonly ParticlePathParameters pathParams;

        public CreateAlongPath(ParticleDefinitionParser parse) : base(parse)
        {
            useRandomCPs = parse.Boolean("m_bUseRandomCPs", useRandomCPs);
            // Modern schema names it m_fMaxDistance; older content uses m_flMaxDistance.
            maxDistance = parse.Float("m_fMaxDistance", parse.Float("m_flMaxDistance", maxDistance));
            pathParams = new ParticlePathParameters(parse);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var path = pathParams;

            if (useRandomCPs)
            {
                var endCp = path.StartControlPointNumber + 1
                    + (int)(particleSystemState.Random.Next() * (path.EndControlPointNumber - path.StartControlPointNumber));
                path = path.WithControlPoints(endCp - 1, endCp);
            }

            var (start, mid, end) = ParticlePath.CalculatePathValues(particleSystemState, path, particle.CreationTime);

            var position = ParticlePath.Evaluate(start, mid, end, particleSystemState.Random.Next());
            position += particleSystemState.Random.NextBetweenPerComponent(new Vector3(-maxDistance), new Vector3(maxDistance));

            particle.SetVector(ParticleField.Position, position);

            return particle;
        }
    }
}
