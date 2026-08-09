namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

/// <summary>
/// Generates a random force within the specified range. A separate force is drawn for every particle,
/// so the effect scatters rather than pushing the whole system one way.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RandomForce">C_OP_RandomForce</seealso>
class RandomForce : ParticleFunctionForceGenerator
{
    private readonly Vector3 min = Vector3.Zero;
    private readonly Vector3 max = Vector3.Zero;


    public RandomForce(ParticleDefinitionParser parse) : base(parse)
    {
        min = parse.Vector3("m_MinForce");
        max = parse.Vector3("m_MaxForce");
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        foreach (ref var particle in particles.Current)
        {
            particle.ForceAccumulator += particleSystemState.Random.NextBetweenPerComponent(min, max) * strength;
        }
    }
}
