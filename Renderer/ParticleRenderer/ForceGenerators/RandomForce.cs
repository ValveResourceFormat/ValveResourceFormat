namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

/// <summary>
/// Generates a random force within the specified range that's applied uniformly to all
/// particles within the effect.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RandomForce">C_OP_RandomForce</seealso>
class RandomForce : ParticleFunctionForceGenerator
{
    private readonly Vector3 Min = Vector3.Zero;
    private readonly Vector3 Max = Vector3.Zero;


    public RandomForce(ParticleDefinitionParser parse) : base(parse)
    {
        Min = parse.Vector3("m_MinForce");
        Max = parse.Vector3("m_MaxForce");
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        // One force is chosen per frame and applied uniformly to every particle
        var force = ParticleCollection.RandomBetweenPerComponent(Min, Max) * strength;

        foreach (ref var particle in particles.Current)
        {
            particle.ForceAccumulator += force;
        }
    }
}
