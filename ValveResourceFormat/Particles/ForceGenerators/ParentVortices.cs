using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Makes every particle of the parent system a vortex that swirls this system's particles around
/// <c>m_vecTwistAxis</c>. The push is <c>m_flForceScale</c> times the parent particle's alpha,
/// fading with the squared distance to nothing at the parent particle's radius.
/// </summary>
/// <remarks>
/// The push is the cross product of the direction from the vortex with the axis, so a particle on
/// the axis line through the vortex gets none. <c>m_bFlipBasedOnYaw</c> negates the position of a
/// vortex whose parent particle's yaw is at least pi, which moves it to the mirrored point rather
/// than reversing its spin.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ParentVortices">C_OP_ParentVortices</seealso>
class ParentVortices : ParticleFunctionForceGenerator
{
    private readonly float forceScale;
    private readonly Vector3 twistAxis = Vector3.UnitZ;
    private readonly bool flipBasedOnYaw;

    public ParentVortices(ParticleDefinitionParser parse) : base(parse)
    {
        forceScale = parse.Float("m_flForceScale", forceScale);
        twistAxis = parse.Vector3("m_vecTwistAxis", twistAxis);
        flipBasedOnYaw = parse.Boolean("m_bFlipBasedOnYaw", flipBasedOnYaw);
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        if (particleSystemState.ParentSystem?.Data is not { } parent || parent.CurrentParticles.IsEmpty)
        {
            return;
        }

        var axis = MathUtils.SafeNormalize(twistAxis);
        var positions = particles.Current.ReadVectors(ParticleField.Position);
        var parentPositions = parent.CurrentParticles.ReadVectors(ParticleField.Position);
        var parentYaws = parent.CurrentParticles.ReadFloats(ParticleField.Yaw);
        var parentRadii = parent.CurrentParticles.ReadFloats(ParticleField.Radius);
        var parentAlphas = parent.CurrentParticles.ReadFloats(ParticleField.Alpha);

        for (var v = 0; v < parentPositions.Length; v++)
        {
            var center = flipBasedOnYaw && parentYaws[v] >= MathF.PI ? -parentPositions[v] : parentPositions[v];
            var inverseRadius = 1f / (parentRadii[v] + 1e-5f);
            var inverseRadiusSquared = inverseRadius * inverseRadius;
            var vortexStrength = strength * forceScale * parentAlphas[v];

            for (var i = 0; i < positions.Length; i++)
            {
                var offset = positions[i] - center;
                var distanceSquared = offset.LengthSquared();

                if (distanceSquared <= ParticleMath.FloatEpsilon)
                {
                    continue;
                }

                var direction = offset / MathF.Sqrt(distanceSquared);
                var tangent = direction - (Vector3.Dot(direction, axis) * direction);

                if (tangent.LengthSquared() <= ParticleMath.FloatEpsilon)
                {
                    continue;
                }

                var falloff = MathF.Max(0f, 1f - (distanceSquared * inverseRadiusSquared)) * vortexStrength;
                forces[i] += Vector3.Cross(Vector3.Normalize(tangent), axis) * falloff;
            }
        }
    }
}
