namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pushes particles away from impulses the game reports, such as explosions and rope hits. Without
/// a game there are no impulses, so it adds no force.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ExternalGameImpulseForce">C_OP_ExternalGameImpulseForce</seealso>
class ExternalGameImpulseForce : ParticleFunctionForceGenerator
{
    public ExternalGameImpulseForce(ParticleDefinitionParser parse) : base(parse)
    {
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
    }
}
