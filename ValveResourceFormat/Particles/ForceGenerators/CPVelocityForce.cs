namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Accelerates every particle by a control point's velocity over the last step, scaled per
/// particle by <c>m_flScale</c>, so particles drift faster the longer the control point moves.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_CPVelocityForce">C_OP_CPVelocityForce</seealso>
class CPVelocityForce : ParticleFunctionForceGenerator
{
    private readonly int controlPoint;
    private readonly INumberProvider scale = new LiteralNumberProvider(1f);

    public CPVelocityForce(ParticleDefinitionParser parse) : base(parse)
    {
        controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
        scale = parse.NumberProvider("m_flScale", scale);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var velocity = particleSystemState.GetControlPoint(controlPoint).StepDelta / frameTime;

        foreach (ref var particle in particles.Current)
        {
            particle.ForceAccumulator += velocity * (strength * scale.NextNumber(ref particle, particleSystemState));
        }
    }
}
