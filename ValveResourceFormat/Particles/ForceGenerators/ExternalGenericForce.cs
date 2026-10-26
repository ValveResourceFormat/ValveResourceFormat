namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pushes particles with the linear, radial, rotational and curl forces of the force fields the game
/// places in the world. Without a game there are no force fields, so it adds no force.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/particles/C_OP_ExternalGenericForce">C_OP_ExternalGenericForce</seealso>
class ExternalGenericForce : ParticleFunctionForceGenerator
{
    public ExternalGenericForce(ParticleDefinitionParser parse) : base(parse)
    {
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
    }
}
