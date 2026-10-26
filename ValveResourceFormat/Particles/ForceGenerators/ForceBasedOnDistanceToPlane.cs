using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Applies a force blended from <c>m_vecForceAtMinDist</c> to <c>m_vecForceAtMaxDist</c> by the
/// particle's signed distance to a plane through a control point, raised to <c>m_flExponent</c>.
/// </summary>
/// <remarks>
/// The plane normal is used as authored, so a longer normal scales the distance. The exponent acts
/// in quarter steps. When the two distances are equal every particle sits at the near end of the
/// blend, which with an exponent of 0 gives the far force.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ForceBasedOnDistanceToPlane">C_OP_ForceBasedOnDistanceToPlane</seealso>
class ForceBasedOnDistanceToPlane : ParticleFunctionForceGenerator
{
    private readonly float minDist;
    private readonly Vector3 forceAtMinDist;
    private readonly float maxDist = 1f;
    private readonly Vector3 forceAtMaxDist;
    private readonly Vector3 planeNormal = Vector3.UnitZ;
    private readonly int controlPoint;
    private readonly float exponent = 1f;

    public ForceBasedOnDistanceToPlane(ParticleDefinitionParser parse) : base(parse)
    {
        minDist = parse.Float("m_flMinDist", minDist);
        forceAtMinDist = parse.Vector3("m_vecForceAtMinDist", forceAtMinDist);
        maxDist = parse.Float("m_flMaxDist", maxDist);
        forceAtMaxDist = parse.Vector3("m_vecForceAtMaxDist", forceAtMaxDist);
        planeNormal = parse.Vector3("m_vecPlaneNormal", planeNormal);
        controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
        exponent = ParticleMath.TruncateToQuarterSteps(parse.Float("m_flExponent", exponent));
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var range = maxDist - minDist;
        var inverseRange = range != 0f ? 1f / range : 0f;
        var origin = particleSystemState.GetControlPoint(controlPoint).Position;
        var positions = particles.Current.ReadVectors(ParticleField.Position);

        for (var i = 0; i < positions.Length; i++)
        {
            var distance = Vector3.Dot(positions[i] - origin, planeNormal);
            var t = MathUtils.Saturate((distance - minDist) * inverseRange);
            var blend = 1f;

            // A particle on the near plane takes no blend even when a negative exponent would make it infinite.
            // A zero base with a negative exponent becomes a blend of about 8.4e6 in the format, which is not reproduced
            if (exponent != 0f)
            {
                blend = t == 0f ? 0f : MathF.Pow(t, exponent);
            }

            forces[i] += MathUtils.Lerp(forceAtMinDist, forceAtMaxDist, blend) * strength;
        }
    }
}
