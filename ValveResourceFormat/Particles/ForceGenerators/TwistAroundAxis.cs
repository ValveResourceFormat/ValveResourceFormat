using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.ForceGenerators;

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
    private readonly int axisControlPoint;

    public TwistAroundAxis(ParticleDefinitionParser parse) : base(parse)
    {
        forceAmount = parse.Float("m_fForceAmount", forceAmount);
        twistAxis = parse.Vector3("m_TwistAxis", twistAxis);
        localSpace = parse.Boolean("m_bLocalSpace", localSpace);
        controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
        axisControlPoint = parse.BehaviorVersion >= 3 ? controlPoint : 0;
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var axis = localSpace
            ? ControlPointTransformProvider.TransformDirection(particleSystemState, axisControlPoint, twistAxis)
            : twistAxis;

        axis = MathUtils.SafeNormalize(axis, Vector3.UnitZ);

        var center = particleSystemState.GetControlPoint(controlPoint).Position;
        var amount = forceAmount * strength;

        var positions = particles.Current.ReadVectors(ParticleField.Position);

        for (var i = 0; i < positions.Length; i++)
        {
            var delta = positions[i] - center;

            if (delta.LengthSquared() <= ParticleMath.FloatEpsilon)
            {
                continue;
            }

            var direction = MathUtils.SafeNormalize(delta);
            var alignment = 1f - Vector3.Dot(direction, axis);

            if (alignment * alignment <= ParticleMath.FloatEpsilon)
            {
                continue;
            }

            forces[i] += Vector3.Cross(direction, axis) * amount;
        }
    }
}
