using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts C_OP_RenderModels' m_bUseRawMeshGroup bool into the m_nSubModelFieldType enum
/// string, written on every matched renderer.
/// </summary>
internal sealed class Vpcf59ToVpcf60 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf59";
    public override string ToFormat => "vpcf60";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_RenderModels")
            {
                return;
            }

            var raw = node.GetBool("m_bUseRawMeshGroup", false);
            node.Remove("m_bUseRawMeshGroup");
            node.SetString("m_nSubModelFieldType", raw ? "SUBMODEL_AS_MESHGROUP_MASK" : "SUBMODEL_AS_BODYGROUP_SUBMODEL");
        });
    }
}
