using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Swaps four legacy integer enum fields to their enum-string spellings in place:
/// m_nOrientationType on every object, and m_nLightType, m_nHand and m_nSortMethod on their
/// renderer classes. Values outside each table and existing strings are left untouched.
/// </summary>
internal sealed class Vpcf28ToVpcf29 : ParticleUpgradeStep
{
    private static readonly Dictionary<long, string> OrientationTypes = new()
    {
        [0] = "PARTICLE_ORIENTATION_SCREEN_ALIGNED",
        [1] = "PARTICLE_ORIENTATION_SCREEN_Z_ALIGNED",
        [2] = "PARTICLE_ORIENTATION_WORLD_Z_ALIGNED",
        [3] = "PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL",
        [4] = "PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL",
        [5] = "PARTICLE_ORIENTATION_FULL_3AXIS_ROTATION",
    };

    private static readonly Dictionary<long, string> LightTypes = new()
    {
        [0] = "PARTICLE_LIGHT_TYPE_POINT",
        [1] = "PARTICLE_LIGHT_TYPE_SPOT",
        [2] = "PARTICLE_LIGHT_TYPE_FX",
    };

    private static readonly Dictionary<long, string> Hands = new()
    {
        [0] = "PARTICLE_VRHAND_LEFT",
        [1] = "PARTICLE_VRHAND_RIGHT",
        [2] = "PARTICLE_VRHAND_CP",
        [3] = "PARTICLE_VRHAND_CP_OBJECT",
    };

    private static readonly Dictionary<long, string> SortMethods = new()
    {
        [0] = "PARTICLE_SORTING_NEAREST",
        [1] = "PARTICLE_SORTING_CREATION_TIME",
    };

    public override string FromFormat => "vpcf28";
    public override string ToFormat => "vpcf29";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            SwapEnum(node, "m_nOrientationType", OrientationTypes);

            switch (node.GetString("_class", ""))
            {
                case "C_OP_RenderStandardLight":
                    SwapEnum(node, "m_nLightType", LightTypes);
                    break;
                case "C_OP_RenderVRHapticEvent":
                    SwapEnum(node, "m_nHand", Hands);
                    break;
                case "C_OP_RenderSprites":
                    SwapEnum(node, "m_nSortMethod", SortMethods);
                    break;
                default:
                    break;
            }
        });
    }

    private static void SwapEnum(KVObject node, string name, Dictionary<long, string> table)
    {
        var value = node.Find(name);

        if (value == null || value.ValueType == KVValueType.String)
        {
            return;
        }

        if (table.TryGetValue(node.GetInt(name, 0), out var replacement))
        {
            node.SetString(name, replacement);
        }
    }
}
