namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

/// <summary>
/// Adds a vector noise sample as a direct force, despite the class name: the noise type selects
/// the vector lattice, the curl combination, or the offset to the nearest worley feature point,
/// sampled at the particle position scaled by the frequency plus the time-driven offset. No
/// derivative or timestep is involved; integration happens in the movement operator.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CurlNoiseForce">C_OP_CurlNoiseForce</seealso>
class CurlNoiseForce : ParticleFunctionForceGenerator
{
    private readonly ParticleDirectionNoiseType noiseType;
    private readonly IVectorProvider noiseFrequency = new LiteralVectorProvider(new Vector3(0.02f));

    /// <summary>Amplitude of the noise.</summary>
    private readonly IVectorProvider noiseScale = new LiteralVectorProvider(new Vector3(1000f));

    private readonly IVectorProvider offset = new LiteralVectorProvider(Vector3.Zero);
    private readonly IVectorProvider offsetRate = new LiteralVectorProvider(Vector3.Zero);
    private readonly INumberProvider worleySeed = new LiteralNumberProvider(0f);
    private readonly INumberProvider worleyJitter = new LiteralNumberProvider(0.875f);

    public CurlNoiseForce(ParticleDefinitionParser parse) : base(parse)
    {
        noiseType = parse.Enum("m_nNoiseType", noiseType);
        noiseFrequency = parse.VectorProvider("m_vecNoiseFreq", noiseFrequency);
        noiseScale = parse.VectorProvider("m_vecNoiseScale", noiseScale);
        offset = parse.VectorProvider("m_vecOffset", offset);
        offsetRate = parse.VectorProvider("m_vecOffsetRate", offsetRate);
        worleySeed = parse.NumberProvider("m_flWorleySeed", worleySeed);
        worleyJitter = parse.NumberProvider("m_flWorleyJitter", worleyJitter);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        foreach (ref var particle in particles.Current)
        {
            var sample = offset.NextVector(ref particle, particleSystemState)
                + (particleSystemState.Age * offsetRate.NextVector(ref particle, particleSystemState))
                + (particle.Position * noiseFrequency.NextVector(ref particle, particleSystemState));

            var noise = noiseType switch
            {
                ParticleDirectionNoiseType.PARTICLE_DIR_NOISE_CURL => Utils.Noise.Curl3D(sample),
                ParticleDirectionNoiseType.PARTICLE_DIR_NOISE_WORLEY_BASIC => Utils.Noise.WorleyOffset3D(
                    sample,
                    worleyJitter.NextNumber(ref particle, particleSystemState),
                    worleySeed.NextNumber(ref particle, particleSystemState)),
                _ => Utils.Noise.ValueVector3(sample),
            };

            particle.ForceAccumulator += noise * noiseScale.NextVector(ref particle, particleSystemState) * strength;
        }
    }
}
