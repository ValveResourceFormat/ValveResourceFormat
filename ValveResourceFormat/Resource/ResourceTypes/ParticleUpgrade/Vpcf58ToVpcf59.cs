using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Moves C_OP_RenderProjected's single m_hProjectedMaterial into a m_vecProjectedMaterials
/// array holding one entry with a resource-flagged m_hMaterial.
/// </summary>
internal sealed class Vpcf58ToVpcf59 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf58";
    public override string ToFormat => "vpcf59";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_RenderProjected")
            {
                return;
            }

            var material = node.GetString("m_hProjectedMaterial", "materials/particle/base_projected.vmat");
            node.Remove("m_hProjectedMaterial");

            var materials = node.Find("m_vecProjectedMaterials");

            if (materials is not { IsArray: true })
            {
                materials = KVObject.Array(1);
                node.SetMember("m_vecProjectedMaterials", materials);
            }

            var element = KVObject.ListCollection(1);
            element.SetMember("m_hMaterial", new KVObject(material) { Flag = KVFlag.Resource });
            materials.Add(element);
        });
    }
}
