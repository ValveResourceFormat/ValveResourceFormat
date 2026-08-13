using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Converts m_bFogParticles on every spritecard renderer into the m_nFogType enum string,
/// adding the game-default value even when the bool was absent.
/// </summary>
internal sealed class Vpcf34ToVpcf35 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf34";
    public override string ToFormat => "vpcf35";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");

            if (cls is not ("C_OP_RenderSprites" or "C_OP_RenderRopes" or "C_OP_RenderTrails"))
            {
                return;
            }

            var fog = node.GetBool("m_bFogParticles", false);
            node.Remove("m_bFogParticles");
            node.SetString("m_nFogType", fog ? "PARTICLE_FOG_ENABLED" : "PARTICLE_FOG_GAME_DEFAULT");
        });
    }
}
