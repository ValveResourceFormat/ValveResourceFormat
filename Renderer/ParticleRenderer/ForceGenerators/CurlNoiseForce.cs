using ValveResourceFormat.Renderer.Particles.Operators;

namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

class CurlNoiseForce : ParticleFunctionOperator
{
    private readonly IVectorProvider NoiseFrequency = new LiteralVectorProvider(Vector3.One);
    private readonly IVectorProvider NoiseScale = new LiteralVectorProvider(Vector3.One);
    private readonly INumberProvider Strength = new LiteralNumberProvider(1.0f);

    public CurlNoiseForce(ParticleDefinitionParser parse) : base(parse)
    {
        NoiseFrequency = parse.VectorProvider("m_vecNoiseFreq", NoiseFrequency);
        NoiseScale = parse.VectorProvider("m_vecNoiseScale", NoiseScale);
        Strength = parse.NumberProvider("m_flOpStrength", Strength);
    }

    public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
    {
        var freq = NoiseFrequency.NextVector(particleSystemState);
        var scale = NoiseScale.NextVector(particleSystemState);
        var strength = Strength.NextNumber(particleSystemState);

        foreach (ref var particle in particles.Current)
        {
            var pos = particle.Position;
            var noisePos = pos * freq;

            // Compute curl noise
            var curl = ComputeCurlNoise(noisePos);

            // Apply scale and strength
            var force = curl * scale * strength;

            particle.Velocity += force * frameTime;
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
        // Simple 3D noise vector field using the existing pseudo-random function
        // Each component uses different frequency combinations for variation
        var fx = Utils.Noise.Simplex1D(pos.X * 0.01f + pos.Y * 0.005f + pos.Z * 0.002f);
        var fy = Utils.Noise.Simplex1D(pos.X * 0.007f + pos.Y * 0.01f + pos.Z * 0.003f);
        var fz = Utils.Noise.Simplex1D(pos.X * 0.004f + pos.Y * 0.008f + pos.Z * 0.01f);

        return new Vector3(fx, fy, fz);
    }
}
