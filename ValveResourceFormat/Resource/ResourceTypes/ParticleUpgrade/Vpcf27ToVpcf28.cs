using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Folds the legacy texture, sequence-combine and blend-mode fields of the spritecard
/// renderers into m_vecTexturesInput entries plus m_nOutputBlendMode. Two-sequence combine
/// modes emit a second entry with per-entry channel masks, and motion-vector and normal-map
/// textures append typed entries.
/// </summary>
internal sealed class Vpcf27ToVpcf28 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf27";
    public override string ToFormat => "vpcf28";

    private static readonly string[] RemovedMembers =
    [
        "m_hTexture",
        "m_bAdditive",
        "m_bAdditiveAlpha",
        "m_bMod2X",
        "m_bLightenMode",
        "m_bMaxLuminanceBlendingSequence1",
        "m_nSequenceCombineMode",
        "m_flSequence0RGBWeight",
        "m_flSequence0AlphaWeight",
        "m_flSequence1RGBWeight",
        "m_flSequence1AlphaWeight",
        "m_flAnimationRate2",
        "m_hMotionVectorsTexture",
        "m_bMotionVectors",
        "m_hNormalTexture",
        "m_bNormalMap",
        "m_flZoomAmount0",
        "m_flZoomAmount1",
        "m_flFinalTextureScaleU",
        "m_flFinalTextureScaleV",
    ];

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");

            if (cls is not ("C_OP_RenderSprites" or "C_OP_RenderRopes" or "C_OP_RenderTrails"))
            {
                return;
            }

            var texture = node.GetString("m_hTexture", "materials/particle/base_sprite.vtex");
            var motionVectorsTexture = node.GetString("m_hMotionVectorsTexture", "");
            var normalTexture = node.GetString("m_hNormalTexture", "");
            var combineMode = node.GetString("m_nSequenceCombineMode", "SEQUENCE_COMBINE_MODE_USE_SEQUENCE_0");
            var additive = node.GetBool("m_bAdditive", false);
            var mod2X = node.GetBool("m_bMod2X", false);
            var lighten = node.GetBool("m_bLightenMode", false);
            var motionVectors = node.GetBool("m_bMotionVectors", false);
            var normalMap = node.GetBool("m_bNormalMap", false);
            var zoom0 = node.GetFloat("m_flZoomAmount0", 1f);
            var zoom1 = node.GetFloat("m_flZoomAmount1", 1f);
            var animationRate = node.GetFloat("m_flAnimationRate", 1f);
            var animationRate2 = node.GetFloat("m_flAnimationRate2", 0f);
            var scaleU = node.GetFloat("m_flFinalTextureScaleU", 1f);
            var scaleV = node.GetFloat("m_flFinalTextureScaleV", 1f);

            var twoSequences = false;
            var textureBlend = -1f;
            var channels0 = "SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA";
            var channels1 = "SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA";

            switch (combineMode)
            {
                case "SEQUENCE_COMBINE_MODE_ALPHA_FROM0_RGB_FROM_1":
                    twoSequences = true;
                    channels0 = "SPRITECARD_TEXTURE_CHANNEL_MIX_A";
                    channels1 = "SPRITECARD_TEXTURE_CHANNEL_MIX_RGB";
                    break;
                case "SEQUENCE_COMBINE_MODE_ALPHA_FROM1_RGB_FROM_0":
                    twoSequences = true;
                    channels0 = "SPRITECARD_TEXTURE_CHANNEL_MIX_RGB";
                    channels1 = "SPRITECARD_TEXTURE_CHANNEL_MIX_A";
                    break;
                case "SEQUENCE_COMBINE_MODE_WEIGHTED_BLEND":
                    var rgbWeight = node.GetFloat("m_flSequence1RGBWeight", 0.5f);
                    var alphaWeight = node.GetFloat("m_flSequence1AlphaWeight", 0.5f);

                    if (rgbWeight == 1f && alphaWeight == 0f)
                    {
                        twoSequences = true;
                        channels0 = "SPRITECARD_TEXTURE_CHANNEL_MIX_A";
                        channels1 = "SPRITECARD_TEXTURE_CHANNEL_MIX_RGB";
                    }

                    break;
                case "SEQUENCE_COMBINE_MODE_USE_SEQUENCE_1":
                    twoSequences = true;
                    textureBlend = 0f;
                    channels0 = "SPRITECARD_TEXTURE_CHANNEL_MIX_A";
                    break;
                default:
                    break;
            }

            foreach (var name in RemovedMembers)
            {
                node.Remove(name);
            }

            var textures = node.Find("m_vecTexturesInput");

            if (textures is not { IsArray: true })
            {
                textures = KVObject.Array();
                node.SetMember("m_vecTexturesInput", textures);
            }

            var entry0 = KVObject.ListCollection();
            textures.Add(entry0);
            entry0.SetMember("m_hTexture", new KVObject(texture) { Flag = KVFlag.Resource });

            if (zoom0 != 1f)
            {
                SetLiteral(GetControls(entry0).SetObject("m_flZoomMax"), zoom0);
            }

            if (scaleU != 1f && scaleU != 0f)
            {
                SetLiteral(GetControls(entry0).SetObject("m_flFinalTextureScaleU"), 1f / scaleU);
            }

            if (scaleV != 1f && scaleV != 0f)
            {
                SetLiteral(GetControls(entry0).SetObject("m_flFinalTextureScaleV"), 1f / scaleV);
            }

            if (twoSequences)
            {
                if (textureBlend != -1f)
                {
                    SetLiteral(entry0.SetObject("m_flTextureBlend"), textureBlend);
                }

                entry0.SetString("m_nTextureChannels", channels0);

                var entry1 = KVObject.ListCollection();
                textures.Add(entry1);
                entry1.SetMember("m_hTexture", new KVObject(texture) { Flag = KVFlag.Resource });

                if (zoom1 != 1f)
                {
                    SetLiteral(GetControls(entry1).SetObject("m_flZoomScale"), zoom1);
                }

                if (animationRate2 != 0f)
                {
                    SetLiteral(GetControls(entry1).SetObject("m_flFinalTextureUVRotation"), animationRate2 * animationRate);
                }

                entry1.SetString("m_nTextureChannels", channels1);
            }

            if (motionVectors && motionVectorsTexture.Length > 0)
            {
                var entry = KVObject.ListCollection();
                textures.Add(entry);
                entry.SetMember("m_hTexture", new KVObject(motionVectorsTexture) { Flag = KVFlag.Resource });
                entry.SetString("m_nTextureType", "SPRITECARD_TEXTURE_ANIMMOTIONVEC");
            }

            if (normalMap && normalTexture.Length > 0)
            {
                var entry = KVObject.ListCollection();
                textures.Add(entry);
                entry.SetMember("m_hTexture", new KVObject(normalTexture) { Flag = KVFlag.Resource });
                entry.SetString("m_nTextureType", "SPRITECARD_TEXTURE_NORMALMAP");
            }

            node.SetString("m_nOutputBlendMode",
                mod2X ? "PARTICLE_OUTPUT_BLEND_MODE_MOD2X" :
                additive ? "PARTICLE_OUTPUT_BLEND_MODE_ADD" :
                lighten ? "PARTICLE_OUTPUT_BLEND_MODE_LIGHTEN" :
                "PARTICLE_OUTPUT_BLEND_MODE_ALPHA");
        });
    }

    private static KVObject GetControls(KVObject entry)
    {
        var controls = entry.Find("m_TextureControls");

        if (controls == null)
        {
            controls = KVObject.ListCollection();
            entry.Add("m_TextureControls", controls);
        }

        return controls;
    }

    private static void SetLiteral(KVObject input, float value)
    {
        input.SetString("m_nType", "PF_TYPE_LITERAL");
        input.SetFloat("m_flLiteralValue", value);
    }
}
