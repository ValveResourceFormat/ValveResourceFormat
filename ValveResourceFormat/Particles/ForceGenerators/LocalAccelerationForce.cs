namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Accelerates particles by a vector given in a control point's local space, so it turns with the
/// control point. Without a control point the vector is applied as authored, in world space.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LocalAccelerationForce">C_OP_LocalAccelerationForce</seealso>
class LocalAccelerationForce : ParticleFunctionForceGenerator
{
    private readonly int controlPoint;
    private readonly int scaleControlPoint = -1;
    private readonly IVectorProvider acceleration = new LiteralVectorProvider(Vector3.Zero);

    public LocalAccelerationForce(ParticleDefinitionParser parse) : base(parse)
    {
        controlPoint = parse.Int32("m_nCP", controlPoint);
        scaleControlPoint = parse.Int32("m_nScaleCP", scaleControlPoint);
        acceleration = parse.VectorProvider("m_vecAccel", acceleration);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var force = acceleration.NextVector(particleSystemState);

        if (controlPoint >= 0)
        {
            if (scaleControlPoint >= 0)
            {
                force *= particleSystemState.GetControlPoint(scaleControlPoint).Position;
            }

            force = ControlPointTransformProvider.TransformDirection(particleSystemState, controlPoint, force);
        }

        foreach (ref var particle in particles.Current)
        {
            particle.ForceAccumulator += force;
        }
    }
}
