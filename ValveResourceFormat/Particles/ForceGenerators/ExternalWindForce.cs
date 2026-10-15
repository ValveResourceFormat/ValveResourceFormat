namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pushes particles with the wind, water flow and gravity the game reports at
/// <c>m_vecSamplePosition</c>, scaled per component by <c>m_vecScale</c>. Without a game there is
/// no wind, water or gravity to sample, which leaves only <c>m_vecGravityForce</c>, or with
/// <c>m_bSampleGravity</c> the removal of the <c>C_OP_BasicMovement</c> gravity when
/// <c>m_bUseBasicMovementGravity</c> is set.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ExternalWindForce">C_OP_ExternalWindForce</seealso>
class ExternalWindForce : ParticleFunctionForceGenerator
{
    private readonly IVectorProvider scale = new LiteralVectorProvider(Vector3.One);
    private readonly bool sampleGravity;
    private readonly IVectorProvider gravityForce = new LiteralVectorProvider(Vector3.Zero);
    private readonly bool useBasicMovementGravity;

    public ExternalWindForce(ParticleDefinitionParser parse) : base(parse)
    {
        scale = parse.VectorProvider("m_vecScale", scale);
        sampleGravity = parse.Boolean("m_bSampleGravity", sampleGravity);
        gravityForce = parse.VectorProvider("m_vecGravityForce", gravityForce);
        useBasicMovementGravity = parse.Boolean("m_bUseBasicMovementGravity", useBasicMovementGravity);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var movementGravity = useBasicMovementGravity ? MovementGravity : Vector3.Zero;

        foreach (ref var particle in particles.Current)
        {
            var force = sampleGravity ? -movementGravity : gravityForce.NextVector(ref particle, particleSystemState);
            particle.ForceAccumulator += scale.NextVector(ref particle, particleSystemState) * force;
        }
    }
}
