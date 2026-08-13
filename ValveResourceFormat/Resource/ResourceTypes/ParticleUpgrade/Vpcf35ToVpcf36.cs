using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 11 to 12 unless some object anywhere in the document has a
/// direct object member typed PF_TYPE_PARTICLE_NUMBER_NORMALIZED. Input blocks sitting in
/// arrays are not seen by that scan and never block.
/// </summary>
internal sealed class Vpcf35ToVpcf36 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf35";
    public override string ToFormat => "vpcf36";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 11)
        {
            return;
        }

        if (HasPoison(root))
        {
            return;
        }

        root.SetInt("m_nBehaviorVersion", 12);
    }

    private static bool HasPoison(KVObject node)
    {
        if (node.IsCollection)
        {
            foreach (var child in node.Values)
            {
                if (child is { IsCollection: true }
                    && child.GetString("m_nType", "PF_TYPE_LITERAL") == "PF_TYPE_PARTICLE_NUMBER_NORMALIZED")
                {
                    return true;
                }
            }

            foreach (var child in node.Values)
            {
                if (HasPoison(child))
                {
                    return true;
                }
            }
        }
        else if (node.IsArray)
        {
            foreach (var element in node.Values)
            {
                if (HasPoison(element))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
