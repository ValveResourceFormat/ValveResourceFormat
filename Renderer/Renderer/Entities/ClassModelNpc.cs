namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// An NPC of Half-Life: Alyx. The class gives the model, and the model configs dress it from
/// the <c>config_state</c> of the <c>model_state</c>. AI is not run.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CNPC_VRCitizenMale">CNPC_VRCitizenMale</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CNPC_VRCitizenFemale">CNPC_VRCitizenFemale</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CZombie">CZombie</seealso>
public sealed class ClassModelNpc : BaseModelEntity
{
    /// <summary>Initializes an NPC from its keyvalues.</summary>
    /// <param name="system">The entity system.</param>
    /// <param name="spawnInfo">The keyvalues.</param>
    /// <param name="modelName">The model its class gives it.</param>
    public ClassModelNpc(EntitySystem system, EntitySpawnInfo spawnInfo, string modelName) : base(system, spawnInfo)
    {
        ModelName = modelName;
    }

    // Collides as a hull, not with the ragdoll its model carries
    /// <inheritdoc/>
    protected override bool BuildsCollider => false;
}
