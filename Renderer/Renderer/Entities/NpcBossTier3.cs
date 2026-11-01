using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The Patron of Deadlock.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/deadlock/server/CNPC_Boss_Tier3">CNPC_Boss_Tier3</seealso>
public sealed class NpcBossTier3 : BaseModelEntity
{
    private readonly string? IdleClip;

    /// <summary>Initializes a Patron from its keyvalues.</summary>
    public NpcBossTier3(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        (ModelName, IdleClip) = KeyValues.GetInt32Property("teamnumber") switch
        {
            2 => ("models/npc/patron_amber/patron_amber.vmdl", "models/npc/patron_amber/clips/idle.vnmclip"),
            3 => ("models/npc/patron_sapphire/patron_sapphire.vmdl", "models/npc/patron_sapphire/clips/patron_sapphire_phase1_idle.vnmclip"),
            _ => (ModelName, null),
        };
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        if (IdleClip != null)
        {
            ModelNode?.SetAnimationForWorldPreview(IdleClip);
        }
    }
}
