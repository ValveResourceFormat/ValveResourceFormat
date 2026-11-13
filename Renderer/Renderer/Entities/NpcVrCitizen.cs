namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A citizen of Half-Life: Alyx. The classname selects the model, and the model configs dress it from
/// the <c>config_state</c> of the <c>model_state</c>. AI is not run.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CNPC_VRCitizenMale">CNPC_VRCitizenMale</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CNPC_VRCitizenFemale">CNPC_VRCitizenFemale</seealso>
public sealed class NpcVrCitizen : BaseModelEntity
{
    /// <summary>Initializes a citizen from its keyvalues.</summary>
    public NpcVrCitizen(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        ModelName = Classname.Equals("npc_vr_citizen_male", StringComparison.OrdinalIgnoreCase)
            ? "models/characters/citizens/citizens_male.vmdl"
            : "models/characters/citizens/citizens_female.vmdl";
    }

    // Collides as a hull, not with the ragdoll its model carries
    /// <inheritdoc/>
    protected override bool BuildsCollider => false;
}
