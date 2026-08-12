namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

/// <summary>
/// Applies a divergence-free curl noise force to particles, producing swirling turbulent motion.
/// Noise frequency, amplitude, and overall strength are configurable.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CurlNoiseForce">C_OP_CurlNoiseForce</seealso>
class CurlNoiseForce : ParticleFunctionForceGenerator
{
    private readonly IVectorProvider noiseFrequency = new LiteralVectorProvider(new Vector3(0.02f));

    /// <summary>Amplitude of the noise.</summary>
    private readonly IVectorProvider noiseScale = new LiteralVectorProvider(new Vector3(1000f));

    public CurlNoiseForce(ParticleDefinitionParser parse) : base(parse)
    {
        noiseFrequency = parse.VectorProvider("m_vecNoiseFreq", noiseFrequency);
        noiseScale = parse.VectorProvider("m_vecNoiseScale", noiseScale);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        var freq = noiseFrequency.NextVector(particleSystemState);
        var scale = noiseScale.NextVector(particleSystemState);

        foreach (ref var particle in particles.Current)
        {
            var pos = particle.Position;
            var noisePos = pos * freq;

            var curl = ComputeCurlNoise(noisePos);

            var force = curl * scale * strength;

            particle.ForceAccumulator += force;
        }
    }

    private static Vector3 ComputeCurlNoise(Vector3 pos)
    {
        // Use finite differences to compute curl of noise field
        const float eps = 0.01f;

        // Sample noise field F at neighboring points
        var F_x0 = NoiseField(pos - new Vector3(eps, 0, 0)); // F at (x-eps, y, z)
        var F_x1 = NoiseField(pos + new Vector3(eps, 0, 0)); // F at (x+eps, y, z)
        var F_y0 = NoiseField(pos - new Vector3(0, eps, 0)); // F at (x, y-eps, z)
        var F_y1 = NoiseField(pos + new Vector3(0, eps, 0)); // F at (x, y+eps, z)
        var F_z0 = NoiseField(pos - new Vector3(0, 0, eps)); // F at (x, y, z-eps)
        var F_z1 = NoiseField(pos + new Vector3(0, 0, eps)); // F at (x, y, z+eps)

        // Partial derivatives
        var dFx_dy = (F_y1.X - F_y0.X) / (2 * eps);
        var dFx_dz = (F_z1.X - F_z0.X) / (2 * eps);
        var dFy_dx = (F_x1.Y - F_x0.Y) / (2 * eps);
        var dFy_dz = (F_z1.Y - F_z0.Y) / (2 * eps);
        var dFz_dx = (F_x1.Z - F_x0.Z) / (2 * eps);
        var dFz_dy = (F_y1.Z - F_y0.Z) / (2 * eps);

        // Curl: (dFz/dy - dFy/dz, dFx/dz - dFz/dx, dFy/dx - dFx/dy)
        return new Vector3(
            dFz_dy - dFy_dz,
            dFx_dz - dFz_dx,
            dFy_dx - dFx_dy
        );
    }

    private static Vector3 NoiseField(Vector3 pos)
    {
        // Three decorrelated samples of the 3D value-noise lattice form the vector field.
        return new Vector3(
            Utils.Noise.Value3D(pos),
            Utils.Noise.Value3D(pos + new Vector3(31.416f, 0f, 0f)),
            Utils.Noise.Value3D(pos + new Vector3(0f, 0f, 47.853f))
        );
    }
}
