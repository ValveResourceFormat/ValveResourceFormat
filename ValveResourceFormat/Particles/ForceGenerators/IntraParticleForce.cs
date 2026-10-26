using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pulls the particles of the system toward each other and pushes them apart in pairs. Each force
/// is strongest at its minimum distance and falls off linearly in squared distance to nothing at
/// its maximum.
/// </summary>
/// <remarks>
/// With <c>m_bUseAABB</c> only pairs whose boxes, grown by each radius plus
/// <c>m_flAttractionMaxDistance</c>, overlap are considered, in the order
/// <see cref="ParticleObjectTree"/> finds them, so repulsion reaching further than that never acts.
/// Nothing happens before the system has bounds.
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
    private readonly ParticleObjectTree objectTree = new();

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

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        if (particleSystemState.Data?.WorldBoundingBox is not { } bounds)
        {
            return;
        }

        var current = particles.Current;
        var positions = current.ReadVectors(ParticleField.Position);

        if (!useAABB)
        {
            for (var i = 0; i < positions.Length - 1; i++)
            {
                for (var j = i + 1; j < positions.Length; j++)
                {
                    ApplyPairForce(positions, forces, i, j, strength);
                }
            }

            return;
        }

        objectTree.Build(positions, current.ReadFloats(ParticleField.Radius), attractionMaxDistance, bounds);
        var slotToParticle = objectTree.SlotToParticle;

        foreach (var (first, second) in objectTree.Pairs)
        {
            ApplyPairForce(positions, forces, slotToParticle[first], slotToParticle[second], strength);
        }
    }

    /// <remarks>
    /// Each pair weighs 1. The movement's mass controls, which would weight a pair by the product of
    /// both masses, are not modelled.
    /// </remarks>
    private void ApplyPairForce(ReadOnlySpan<Vector3> positions, Span<Vector3> forces, int firstIndex, int secondIndex, float strength)
    {
        var offset = positions[secondIndex] - positions[firstIndex];
        var distanceSquared = offset.LengthSquared();

        if (distanceSquared <= MinimumDistanceSquared)
        {
            return;
        }

        var distance = MathF.Sqrt(distanceSquared);

        if (distance < attractionMaxDistance)
        {
            var pull = offset * (attractionMaxStrength * strength * Falloff(distanceSquared, attractionMinDistance, attractionMaxDistance));
            forces[firstIndex] += pull;
            forces[secondIndex] -= pull;
        }

        if (distance < repulsionMaxDistance)
        {
            var push = offset * (repulsionMaxStrength * strength * Falloff(distanceSquared, repulsionMinDistance, repulsionMaxDistance));
            forces[firstIndex] -= push;
            forces[secondIndex] += push;
        }
    }

    private static float Falloff(float distanceSquared, float minDistance, float maxDistance)
        => MathUtils.RemapValClamped(distanceSquared, minDistance * minDistance, maxDistance * maxDistance, 1f, 0f);
}
