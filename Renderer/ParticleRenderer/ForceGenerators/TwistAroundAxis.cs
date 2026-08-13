using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

/// <summary>
/// Pushes particles tangentially around an axis running through a control point, spinning them about
/// it. The axis is <c>m_TwistAxis</c>, rotated into the control point's frame when <c>m_bLocalSpace</c>
/// is set.
///
/// <para>The force is the cross product of the particle's outward direction with the axis, so it fades
/// out as a particle approaches the axis poles and vanishes for one lying exactly on the axis, rather
/// than staying uniform around it.</para>
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_TwistAroundAxis">C_OP_TwistAroundAxis</seealso>
class TwistAroundAxis : ParticleFunctionForceGenerator
{
    private readonly float forceAmount;
    private readonly Vector3 twistAxis = Vector3.UnitZ;
    private readonly bool localSpace;
    private readonly int controlPoint;

    public TwistAroundAxis(ParticleDefinitionParser parse) : base(parse)
    {
        forceAmount = parse.Float("m_fForceAmount", forceAmount);
        twistAxis = parse.Vector3("m_TwistAxis", twistAxis);
        localSpace = parse.Boolean("m_bLocalSpace", localSpace);
        controlPoint = parse.BehaviorVersion >= 3 ? parse.Int32("m_nControlPointNumber", controlPoint) : 0;
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        var axis = localSpace
            ? ControlPointTransformProvider.TransformDirection(particleSystemState, controlPoint, twistAxis)
            : twistAxis;

        axis = axis == Vector3.Zero ? Vector3.UnitZ : Vector3.Normalize(axis);

        var center = particleSystemState.GetControlPoint(controlPoint).Position;
        var amount = forceAmount * strength;

        foreach (ref var particle in particles.Current)
        {
            var delta = particle.Position - center;

            if (delta.LengthSquared() <= Epsilon.LengthSquared)
            {
                continue;
            }

            var direction = Vector3.Normalize(delta);
            var alignment = 1f - Vector3.Dot(direction, axis);

            if (alignment * alignment <= Epsilon.LengthSquared)
            {
                continue;
            }

            particle.ForceAccumulator += Vector3.Cross(direction, axis) * amount;
        }
    }
}
