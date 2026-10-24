using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pulls the particles of the system toward each other and pushes them apart in pairs. Each force
/// is strongest at its minimum distance and falls off linearly in squared distance to nothing at
/// its maximum.
/// </summary>
/// <remarks>
/// With <c>m_bUseAABB</c> only pairs whose boxes, grown by each radius plus
/// <c>m_flAttractionMaxDistance</c>, overlap are considered, so repulsion reaching further than
/// that never acts. Nothing happens before the system has bounds.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_IntraParticleForce">C_OP_IntraParticleForce</seealso>
class IntraParticleForce : ParticleFunctionForceGenerator
{
    private const float MinimumDistanceSquared = ParticleMath.FloatEpsilon;

    private readonly float attractionMinDistance;
    private readonly float attractionMaxDistance = 1f;
    private readonly float attractionMaxStrength;
    private readonly float repulsionMinDistance = 2f;
    private readonly float repulsionMaxDistance = 4f;
    private readonly float repulsionMaxStrength;
    private readonly bool useAABB = true;

    public IntraParticleForce(ParticleDefinitionParser parse) : base(parse)
    {
        attractionMinDistance = parse.Float("m_flAttractionMinDistance", attractionMinDistance);
        attractionMaxDistance = parse.Float("m_flAttractionMaxDistance", attractionMaxDistance);
        attractionMaxStrength = parse.Float("m_flAttractionMaxStrength", attractionMaxStrength);
        repulsionMinDistance = parse.Float("m_flRepulsionMinDistance", repulsionMinDistance);
        repulsionMaxDistance = parse.Float("m_flRepulsionMaxDistance", repulsionMaxDistance);
        repulsionMaxStrength = parse.Float("m_flRepulsionMaxStrength", repulsionMaxStrength);
        useAABB = parse.Boolean("m_bUseAABB", useAABB);
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        if (particleSystemState.Data?.WorldBoundingBox == null)
        {
            return;
        }

        var current = particles.Current;

        for (var i = 0; i < current.Length - 1; i++)
        {
            for (var j = i + 1; j < current.Length; j++)
            {
                if (useAABB && !BoxesOverlap(current[i], current[j]))
                {
                    continue;
                }

                ApplyPairForce(current[i], current[j], strength);
            }
        }
    }

    private bool BoxesOverlap(Particle first, Particle second)
    {
        var reach = new Vector3(first.Radius + second.Radius + (2f * attractionMaxDistance));
        var offset = Vector3.Abs(second.Position - first.Position);

        return offset.X <= reach.X && offset.Y <= reach.Y && offset.Z <= reach.Z;
    }

    private void ApplyPairForce(Particle first, Particle second, float strength)
    {
        var offset = second.Position - first.Position;
        var distanceSquared = offset.LengthSquared();

        if (distanceSquared <= MinimumDistanceSquared)
        {
            return;
        }

        var distance = MathF.Sqrt(distanceSquared);

        if (distance < attractionMaxDistance)
        {
            var pull = offset * (attractionMaxStrength * strength * Falloff(distanceSquared, attractionMinDistance, attractionMaxDistance));
            first.ForceAccumulator += pull;
            second.ForceAccumulator -= pull;
        }

        if (distance < repulsionMaxDistance)
        {
            var push = offset * (repulsionMaxStrength * strength * Falloff(distanceSquared, repulsionMinDistance, repulsionMaxDistance));
            first.ForceAccumulator -= push;
            second.ForceAccumulator += push;
        }
    }

    private static float Falloff(float distanceSquared, float minDistance, float maxDistance)
        => MathUtils.RemapValClamped(distanceSquared, minDistance * minDistance, maxDistance * maxDistance, 1f, 0f);
}
