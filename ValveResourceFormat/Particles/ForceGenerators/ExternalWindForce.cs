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
    private const int BlocksPerBatch = 100;

    private readonly IVectorProvider scale = new LiteralVectorProvider(Vector3.One);
    private readonly bool sampleGravity;
    private readonly IVectorProvider gravityForce = new LiteralVectorProvider(Vector3.Zero);
    private readonly IVectorProvider buoyancyForce = new LiteralVectorProvider(Vector3.Zero);
    private readonly bool useBasicMovementGravity;
    private readonly INumberProvider localGravityScale = new LiteralNumberProvider(1f);
    private readonly INumberProvider localBuoyancyScale = new LiteralNumberProvider(1f);
    private readonly bool samplesPerParticle;

    public ExternalWindForce(ParticleDefinitionParser parse) : base(parse)
    {
        scale = parse.VectorProvider("m_vecScale", scale);
        sampleGravity = parse.Boolean("m_bSampleGravity", sampleGravity);
        gravityForce = parse.VectorProvider("m_vecGravityForce", gravityForce);
        buoyancyForce = parse.VectorProvider("m_vecBuoyancyForce", buoyancyForce);
        localBuoyancyScale = parse.NumberProvider("m_flLocalBuoyancyScale", localBuoyancyScale);
        useBasicMovementGravity = parse.Boolean("m_bUseBasicMovementGravity", useBasicMovementGravity);
        localGravityScale = parse.NumberProvider("m_flLocalGravityScale", localGravityScale);
        samplesPerParticle = parse.Data.GetSubCollection("m_vecSamplePosition")?.GetStringProperty("m_nType") is
            "PVEC_TYPE_PARTICLE_VECTOR" or "PVEC_TYPE_PARTICLE_INITIAL_VECTOR" or "PVEC_TYPE_PARTICLE_VELOCITY" or "PVEC_TYPE_PARTICLE_GRAVITY";
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var movementGravity = useBasicMovementGravity ? MovementGravity : Vector3.Zero;
        var gravityScale = useBasicMovementGravity ? movementGravity.Z / WorldGravity : 1f;
        var current = particles.Current;
        var scales = scale.Prepare(current, particleSystemState);
        var gravities = gravityForce.Prepare(current, particleSystemState);
        var localGravityScales = localGravityScale.Fill(current, particleSystemState);

        if (samplesPerParticle)
        {
            localBuoyancyScale.Fill(current, particleSystemState);
            FillPerParticleInputs(current, particleSystemState, scales, gravities);
        }
        else
        {
            foreach (var (offset, block, lane) in current.InputBlocks())
            {
                if (!sampleGravity)
                {
                    gravityForce.FillPerBlock(block, lane, particleSystemState, gravities[offset..]);
                }

                scale.FillPerBlock(block, lane, particleSystemState, scales[offset..]);
            }
        }

        // Buoyancy and the sample position are not modelled
        var gravityOffset = samplesPerParticle ? movementGravity : Vector3.Zero;

        for (var i = 0; i < scales.Length; i++)
        {
            var force = sampleGravity
                ? new Vector3(0f, 0f, WorldGravity * localGravityScales[i] * gravityScale) - movementGravity
                : gravities[i] - gravityOffset;
            forces[i] += scales[i] * force;
        }
    }

    /// <summary>
    /// Fills the inputs as a sample position read from each particle evaluates them: in batches of up
    /// to <see cref="BlocksPerBatch"/> blocks, the gravity of every block of the batch first, then
    /// the scale and buoyancy of every block. The buoyancy is drawn and never used.
    /// </summary>
    private void FillPerParticleInputs(ParticleSpan current, ParticleSystemState particleSystemState, Span<Vector3> scales, Span<Vector3> gravities)
    {
        var buoyancies = sampleGravity ? default : buoyancyForce.Prepare(current, particleSystemState);
        var batchStart = 0;

        while (batchStart < current.Length)
        {
            var batchEnd = batchStart;
            var blocks = 0;

            foreach (var (offset, block, lane) in current[batchStart..].InputBlocks())
            {
                if (blocks == BlocksPerBatch)
                {
                    break;
                }

                if (!sampleGravity)
                {
                    gravityForce.FillPerBlock(block, lane, particleSystemState, gravities[(batchStart + offset)..]);
                }

                batchEnd = batchStart + offset + block.Length;
                blocks++;
            }

            foreach (var (offset, block, lane) in current[batchStart..batchEnd].InputBlocks())
            {
                scale.FillPerBlock(block, lane, particleSystemState, scales[(batchStart + offset)..]);

                if (!sampleGravity)
                {
                    buoyancyForce.FillPerBlock(block, lane, particleSystemState, buoyancies[(batchStart + offset)..]);
                }
            }

            batchStart = batchEnd;
        }
    }
}
