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
        private readonly float MaxDistance;
        private readonly bool UseRandomCPs; // randomly select sequential CP pairs between start and end points
        private readonly ParticlePathParameters PathParams;

        public CreateAlongPath(ParticleDefinitionParser parse) : base(parse)
        {
            UseRandomCPs = parse.Boolean("m_bUseRandomCPs", UseRandomCPs);
            // Modern schema names it m_fMaxDistance; older content uses m_flMaxDistance.
            MaxDistance = parse.Float("m_fMaxDistance", parse.Float("m_flMaxDistance", MaxDistance));
            PathParams = new ParticlePathParameters(parse);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var path = PathParams;

            if (UseRandomCPs)
            {
                var endCp = path.StartControlPointNumber + 1
                    + (int)(particleSystemState.NextRandom() * (path.EndControlPointNumber - path.StartControlPointNumber));
                path = path.WithControlPoints(endCp - 1, endCp);
            }

            var (start, mid, end) = ParticlePath.CalculatePathValues(particleSystemState, path, particle.CreationTime);

            var position = ParticlePath.Evaluate(start, mid, end, particleSystemState.NextRandom());
            position += particleSystemState.NextRandomBetweenPerComponent(new Vector3(-MaxDistance), new Vector3(MaxDistance));

            particle.SetVector(ParticleField.Position, position);

            return particle;
        }
    }
}
