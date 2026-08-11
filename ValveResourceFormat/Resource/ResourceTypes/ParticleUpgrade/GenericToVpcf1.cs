using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Renames snapshot name members to m_hSnapshot and rewrites their string values into
/// particles/&lt;name&gt;.vsnap resource paths.
/// </summary>
internal sealed class GenericToVpcf1 : ParticleUpgradeStep
{
    public override string FromFormat => "generic";
    public override string ToFormat => "vpcf1";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");

            if (cls == "CParticleSystemDefinition")
            {
                ConvertSnapshotMember(node, "m_pszSnapshotName");
            }
            else if (cls == "C_OP_InitSetSnapshotOnCP")
            {
                ConvertSnapshotMember(node, "m_snapshotName");
            }
        });
    }

    private static void ConvertSnapshotMember(KVObject node, string oldName)
    {
        var value = node.Rename(oldName, "m_hSnapshot");

        if (value == null)
        {
            return;
        }

        value.Flag = KVFlag.Resource;

        var path = value.ValueType == KVValueType.String ? (string)value! : "";

        if (path.Length == 0)
        {
            return;
        }

        path = path.Replace('\\', '/');

        for (var i = path.Length - 1; i >= 0; i--)
        {
            if (path[i] == '/')
            {
                break;
            }

            if (path[i] == '.')
            {
                path = path[..i];
                break;
            }
        }

        path += ".vsnap";

        if (!path.StartsWith("particles/", StringComparison.Ordinal))
        {
            path = "particles/" + path;
        }

        node.SetMember("m_hSnapshot", new KVObject(path) { Flag = KVFlag.Resource });
    }
}
