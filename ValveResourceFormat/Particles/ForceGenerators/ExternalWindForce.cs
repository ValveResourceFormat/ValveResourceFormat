using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pushes particles with the wind, water flow and gravity the game reports at
/// <c>m_vecSamplePosition</c>, scaled per component by <c>m_vecScale</c>. Without a game there is
/// no wind or water to sample, and sampled gravity is the standard world gravity, so with
/// <c>m_bSampleGravity</c> the force is that gravity scaled by <c>m_flLocalGravityScale</c>, minus the
/// <c>C_OP_BasicMovement</c> gravity when <c>m_bUseBasicMovementGravity</c> is set, and
/// <c>m_vecGravityForce</c> otherwise. A sample position read from the particles takes that
/// movement gravity off <c>m_vecGravityForce</c> as well.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ExternalWindForce">C_OP_ExternalWindForce</seealso>
class ExternalWindForce : ParticleFunctionForceGenerator
{
    private const float WorldGravity = -800f;

    private readonly IVectorProvider scale = new LiteralVectorProvider(Vector3.One);
    private readonly bool sampleGravity;
    private readonly IVectorProvider gravityForce = new LiteralVectorProvider(Vector3.Zero);
    private readonly bool useBasicMovementGravity;
    private readonly INumberProvider localGravityScale = new LiteralNumberProvider(1f);
    private readonly bool samplesPerParticle;

    public ExternalWindForce(ParticleDefinitionParser parse) : base(parse)
    {
        scale = parse.VectorProvider("m_vecScale", scale);
        sampleGravity = parse.Boolean("m_bSampleGravity", sampleGravity);
        gravityForce = parse.VectorProvider("m_vecGravityForce", gravityForce);
        useBasicMovementGravity = parse.Boolean("m_bUseBasicMovementGravity", useBasicMovementGravity);
        localGravityScale = parse.NumberProvider("m_flLocalGravityScale", localGravityScale);
        samplesPerParticle = parse.Data.GetSubCollection("m_vecSamplePosition")?.GetStringProperty("m_nType") is
            "PVEC_TYPE_PARTICLE_VECTOR" or "PVEC_TYPE_PARTICLE_INITIAL_VECTOR" or "PVEC_TYPE_PARTICLE_VELOCITY" or "PVEC_TYPE_PARTICLE_GRAVITY";
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var movementGravity = useBasicMovementGravity ? MovementGravity : Vector3.Zero;
        var gravityScale = useBasicMovementGravity ? movementGravity.Z / WorldGravity : 1f;

        foreach (ref var particle in particles.Current)
        {
            var force = sampleGravity
                ? new Vector3(0f, 0f, WorldGravity * localGravityScale.NextNumber(ref particle, particleSystemState) * gravityScale) - movementGravity
                : gravityForce.NextVector(ref particle, particleSystemState) - (samplesPerParticle ? movementGravity : Vector3.Zero);
            particle.ForceAccumulator += scale.NextVector(ref particle, particleSystemState) * force;
        }
    }
}
