namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Applies a force to every particle, scaled per particle by a float input. The force is authored
/// in a control point's local frame when one is assigned, so it turns with the emitter.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_PerParticleForce">C_OP_PerParticleForce</seealso>
class PerParticleForce : ParticleFunctionForceGenerator
{
    private readonly INumberProvider forceScale = new LiteralNumberProvider(1f);
    private readonly IVectorProvider force = new LiteralVectorProvider(Vector3.Zero);
    private readonly int controlPoint = -1;

    public PerParticleForce(ParticleDefinitionParser parse) : base(parse)
    {
        forceScale = parse.NumberProvider("m_flForceScale", forceScale);
        force = parse.VectorProvider("m_vForce", force);
        controlPoint = parse.Int32("m_nCP", controlPoint);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        foreach (ref var particle in particles.Current)
        {
            var value = force.NextVector(ref particle, particleSystemState);

            if (controlPoint >= 0)
            {
                value = ControlPointTransformProvider.TransformDirection(particleSystemState, controlPoint, value);
            }

            particle.ForceAccumulator += value * forceScale.NextNumber(ref particle, particleSystemState) * strength;
        }
    }
}
