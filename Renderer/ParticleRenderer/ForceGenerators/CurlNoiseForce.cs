namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

/// <summary>
/// Applies a divergence-free curl noise force to particles, producing swirling turbulent motion.
/// Noise frequency, amplitude, and overall strength are configurable.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CurlNoiseForce">C_OP_CurlNoiseForce</seealso>
class CurlNoiseForce : ParticleFunctionForceGenerator
{
    private readonly IVectorProvider NoiseFrequency = new LiteralVectorProvider(new Vector3(0.02f));
    private readonly IVectorProvider NoiseScale = new LiteralVectorProvider(new Vector3(1000f));
    private readonly INumberProvider Strength = new LiteralNumberProvider(1.0f);

    public CurlNoiseForce(ParticleDefinitionParser parse) : base(parse)
    {
        NoiseFrequency = parse.VectorProvider("m_vecNoiseFreq", NoiseFrequency);
        NoiseScale = parse.VectorProvider("m_vecNoiseScale", NoiseScale);
        Strength = parse.NumberProvider("m_flOpStrength", Strength);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        var freq = NoiseFrequency.NextVector(particleSystemState);
        var scale = NoiseScale.NextVector(particleSystemState);
        strength *= Strength.NextNumber(particleSystemState);

        foreach (ref var particle in particles.Current)
        {
            var pos = particle.Position;
            var noisePos = pos * freq;

            // Compute curl noise
            var curl = ComputeCurlNoise(noisePos);

            // Apply scale and strength
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
            Noise3D(pos),
            Noise3D(pos + new Vector3(31.416f, 0f, 0f)),
            Noise3D(pos + new Vector3(0f, 0f, 47.853f))
        );
    }

    // Trilinear value noise over an integer lattice, returning [-1, 1] to match Source's DNoiseSIMD.
    private static float Noise3D(Vector3 pos)
    {
        var ix = (int)MathF.Floor(pos.X);
        var iy = (int)MathF.Floor(pos.Y);
        var iz = (int)MathF.Floor(pos.Z);

        var u = Fade(pos.X - ix);
        var v = Fade(pos.Y - iy);
        var w = Fade(pos.Z - iz);

        var c00 = float.Lerp(Hash(ix, iy, iz), Hash(ix + 1, iy, iz), u);
        var c10 = float.Lerp(Hash(ix, iy + 1, iz), Hash(ix + 1, iy + 1, iz), u);
        var c01 = float.Lerp(Hash(ix, iy, iz + 1), Hash(ix + 1, iy, iz + 1), u);
        var c11 = float.Lerp(Hash(ix, iy + 1, iz + 1), Hash(ix + 1, iy + 1, iz + 1), u);

        var c0 = float.Lerp(c00, c10, v);
        var c1 = float.Lerp(c01, c11, v);

        return (2f * float.Lerp(c0, c1, w)) - 1f;
    }

    private static float Fade(float t) => t * t * t * (t * ((t * 6f) - 15f) + 10f);

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            var h = (x * 374761393) + (y * 668265263) + (z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)int.MaxValue;
        }
    }
}
