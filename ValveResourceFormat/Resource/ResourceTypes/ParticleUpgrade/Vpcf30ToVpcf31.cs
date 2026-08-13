using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Moves C_OP_RenderTrails final texture offsets into literal inputs inside every texture
/// entry's m_TextureControls, iterating entries last to first with a running accumulator that
/// picks up pre-existing literal offsets and carries them into all earlier entries.
/// </summary>
internal sealed class Vpcf30ToVpcf31 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf30";
    public override string ToFormat => "vpcf31";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            if (node.GetString("_class", "") != "C_OP_RenderTrails")
            {
                return;
            }

            var offsetU = node.GetFloat("m_flFinalTextureOffsetU", 0f);
            var offsetV = node.GetFloat("m_flFinalTextureOffsetV", 0f);

            node.Remove("m_flFinalTextureOffsetU");
            node.Remove("m_flFinalTextureOffsetV");

            var textures = node.Find("m_vecTexturesInput");

            if (textures == null)
            {
                return;
            }

            var entries = textures.Elements();

            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];

                if (!UpgradeKV.IsObject(entry))
                {
                    continue;
                }

                if (offsetU != 0f)
                {
                    offsetU = ApplyOffset(entry, "m_flFinalTextureOffsetU", offsetU);
                }

                if (offsetV != 0f)
                {
                    offsetV = ApplyOffset(entry, "m_flFinalTextureOffsetV", offsetV);
                }
            }
        });
    }

    private static float ApplyOffset(KVObject entry, string name, float offset)
    {
        var controls = entry.Find("m_TextureControls");

        if (controls == null)
        {
            controls = KVObject.ListCollection();
            entry.Add("m_TextureControls", controls);
        }

        var existing = controls.Find(name);

        if (existing is { IsCollection: true })
        {
            offset += existing.GetFloat("m_flLiteralValue", 0f);
        }

        var input = controls.SetObject(name);
        input.SetString("m_nType", "PF_TYPE_LITERAL");
        input.SetFloat("m_flLiteralValue", offset);
        return offset;
    }
}
