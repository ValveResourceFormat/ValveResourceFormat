namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

/// <summary>
/// Adds four octaves of vector-valued noise, sampled at the particle's position, as a force. Each
/// octave has its own frequency (<c>m_flNoiseCoordScale0..3</c>) and per-axis amplitude
/// (<c>m_vecNoiseAmount0..3</c>).
///
/// <para>An octave with a frequency of zero samples the lattice at the origin for every particle, so it
/// contributes a constant offset to the whole system rather than nothing; the defaults leave the upper
/// three octaves in exactly that state.</para>
///
/// <para>The operator fade strength is not applied, unlike <see cref="CurlNoiseForce"/>.</para>
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_TurbulenceForce">C_OP_TurbulenceForce</seealso>
class TurbulenceForce : ParticleFunctionForceGenerator
{
    private readonly float noiseCoordScale0 = 1f;
    private readonly float noiseCoordScale1;
    private readonly float noiseCoordScale2;
    private readonly float noiseCoordScale3;

    private readonly Vector3 noiseAmount0 = new(1f);
    private readonly Vector3 noiseAmount1 = new(0.5f);
    private readonly Vector3 noiseAmount2 = new(0.25f);
    private readonly Vector3 noiseAmount3 = new(0.125f);

    public TurbulenceForce(ParticleDefinitionParser parse) : base(parse)
    {
        noiseCoordScale0 = parse.Float("m_flNoiseCoordScale0", noiseCoordScale0);
        noiseCoordScale1 = parse.Float("m_flNoiseCoordScale1", noiseCoordScale1);
        noiseCoordScale2 = parse.Float("m_flNoiseCoordScale2", noiseCoordScale2);
        noiseCoordScale3 = parse.Float("m_flNoiseCoordScale3", noiseCoordScale3);

        noiseAmount0 = parse.Vector3("m_vecNoiseAmount0", noiseAmount0);
        noiseAmount1 = parse.Vector3("m_vecNoiseAmount1", noiseAmount1);
        noiseAmount2 = parse.Vector3("m_vecNoiseAmount2", noiseAmount2);
        noiseAmount3 = parse.Vector3("m_vecNoiseAmount3", noiseAmount3);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        foreach (ref var particle in particles.Current)
        {
            var position = particle.Position;

            var force = noiseAmount0 * Utils.Noise.ValueVector3(position * noiseCoordScale0);
            force += noiseAmount1 * Utils.Noise.ValueVector3(position * noiseCoordScale1);
            force += noiseAmount2 * Utils.Noise.ValueVector3(position * noiseCoordScale2);
            force += noiseAmount3 * Utils.Noise.ValueVector3(position * noiseCoordScale3);

            particle.ForceAccumulator += force;
        }
    }
}
