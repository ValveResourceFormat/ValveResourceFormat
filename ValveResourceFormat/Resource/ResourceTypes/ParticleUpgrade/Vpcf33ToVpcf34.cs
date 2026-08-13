using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Bumps m_nBehaviorVersion from 10 to 11. When the root's own renderer list carries a trail
/// or rope renderer, the whole document is scanned for such renderers whose unprefixed
/// VisibilityInputs block sets a visibility CP or a positive FOV radius base, which blocks
/// the bump; the schema-spelled m_VisibilityInputs never does.
/// </summary>
internal sealed class Vpcf33ToVpcf34 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf33";
    public override string ToFormat => "vpcf34";

    public override void Apply(KVObject root)
    {
        if (root.GetInt("m_nBehaviorVersion", 0) != 10)
        {
            return;
        }

        var gate = false;

        foreach (var element in root.ElementsOf("m_Renderers"))
        {
            if (UpgradeKV.IsObject(element)
                && element.GetString("_class", "") is "C_OP_RenderTrails" or "C_OP_RenderRopes")
            {
                gate = true;
                break;
            }
        }

        if (gate && HasPoison(root))
        {
            return;
        }

        root.SetInt("m_nBehaviorVersion", 11);
    }

    private static bool HasPoison(KVObject node)
    {
        if (node.IsCollection)
        {
            if (node.GetString("_class", "") is "C_OP_RenderTrails" or "C_OP_RenderRopes")
            {
                var visibility = node.Find("VisibilityInputs");

                if (visibility is { IsCollection: true })
                {
                    if (visibility.GetInt("m_nCPin", -1) >= 0)
                    {
                        return true;
                    }

                    if (visibility.GetFloat("m_flRadiusScaleFOVBase", 0f) > 0f)
                    {
                        return true;
                    }
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
