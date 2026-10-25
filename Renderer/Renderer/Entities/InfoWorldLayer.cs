using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_world_layer</c>. Shows or hides a world layer and spawns or destroys the entities in the
/// layer's entity lump, by entity I/O. The entities spawn as a spawn group of their own, which destroying
/// them unloads.
/// </summary>
/// <remarks>
/// <para>
/// The layer belongs to the loaded world named by <see cref="WorldName"/> in this entity's world group, which
/// need not be the world this entity is in. Its group spawns there unplaced and without name fixups, so the
/// entities stay where they were authored in that world.
/// </para>
/// <para>
/// Entities spawned by an input after load are not bound to the scene's cubemaps and light probes,
/// which are only assigned when the scene is first initialized.
/// </para>
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CInfoWorldLayer">CInfoWorldLayer</seealso>
public sealed class InfoWorldLayer : BaseEntity
{
    /// <summary>Spawn flags for world layers.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>The layer is shown and its entities are spawned when the map loads.</summary>
        VisibleOnSpawn = 1,
    }

    /// <summary>Gets the name of the world the layer belongs to.</summary>
    public string? WorldName => KeyValues.GetStringProperty("worldname");

    /// <summary>Gets the name of the world layer this entity controls, which also names its entity lump.</summary>
    public string? WorldLayerName => KeyValues.GetStringProperty("layername");

    /// <summary>Gets whether the controlled layer is shown when the map loads.</summary>
    public bool IsVisibleOnSpawn => HasSpawnFlags(SpawnFlag.VisibleOnSpawn);

    /// <summary>Gets the spawn group of the layer's entities, or <see langword="null"/> when they are not spawned.</summary>
    public SpawnGroup? LayerSpawnGroup { get; private set; }

    /// <summary>Initializes an <c>info_world_layer</c> from its keyvalues.</summary>
    public InfoWorldLayer(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        if (IsVisibleOnSpawn)
        {
            SpawnLayerEntities();
        }
    }

    /// <summary>Shows or hides the controlled layer.</summary>
    public void SetLayerVisible(bool visible)
    {
        if (string.IsNullOrEmpty(WorldLayerName))
        {
            return;
        }

        var scene = FindWorld()?.Scene ?? Scene;

        if (visible)
        {
            scene.ActivateLayer(WorldLayerName);
        }
        else
        {
            scene.DeactivateLayer(WorldLayerName);
        }
    }

    private WorldLoader? FindWorld()
        => string.IsNullOrEmpty(WorldName) ? null : EntitySystem.SpawnGroupManager.FindLoadedWorld(Scene.WorldGroup, WorldLoader.GetSpawnGroupMapName(WorldName));

    /// <summary>
    /// Spawns the entities of the layer's entity lump, unless they already are, and fires
    /// <c>OnEntitiesSpawned</c> once they have.
    /// </summary>
    private void SpawnLayerEntities()
    {
        if (LayerSpawnGroup != null || string.IsNullOrEmpty(WorldName) || string.IsNullOrEmpty(WorldLayerName))
        {
            return;
        }

        if (FindWorld() is not { } world || world.FindEntityLump(WorldLayerName) is not { } lump)
        {
            EntitySystem.Logger.LogWarning("info_world_layer '{TargetName}' found no entity lump named '{LayerName}' in a loaded world named '{WorldName}'", TargetName, WorldLayerName, WorldName);
            return;
        }

        var group = new SpawnGroup(world.MapName, world.Scene, Matrix4x4.Identity)
        {
            DescriptiveName = "world_layer",
            EntityLumpName = WorldLayerName,
            EntityLump = lump,
        };

        LayerSpawnGroup = group;

        var spawnGroupManager = EntitySystem.SpawnGroupManager;
        spawnGroupManager.LoadSpawnGroup(group);

        // A child of the group this entity is in, so it unloads along with that one
        if (SpawnGroup != null && KeyValues.GetBooleanProperty("childspawngroup"))
        {
            spawnGroupManager.AddChildSpawnGroup(SpawnGroup, group);
        }

        spawnGroupManager.ListenForSpawnGroupToFinish(group, _ => EntitySystem.TriggerOutput(this, "OnEntitiesSpawned", this));
        spawnGroupManager.SynchronouslySpawnGroup(group);
    }

    private void DestroyLayerEntities()
    {
        if (LayerSpawnGroup is not { } group)
        {
            return;
        }

        LayerSpawnGroup = null;
        EntitySystem.SpawnGroupManager.UnloadSpawnGroup(group);
    }

    /// <inheritdoc/>
    protected override void OnRemove() => DestroyLayerEntities();

    [EntityInput("ShowWorldLayer")] private void InputShowWorldLayer(EntityInputData data) => SetLayerVisible(true);

    [EntityInput("HideWorldLayer")] private void InputHideWorldLayer(EntityInputData data) => SetLayerVisible(false);

    [EntityInput("SpawnEntities")] private void InputSpawnEntities(EntityInputData data) => SpawnLayerEntities();

    [EntityInput("DestroyEntities")] private void InputDestroyEntities(EntityInputData data) => DestroyLayerEntities();

    [EntityInput("ShowWorldLayerAndSpawnEntities")]
    private void InputShowWorldLayerAndSpawnEntities(EntityInputData data)
    {
        SpawnLayerEntities();
        SetLayerVisible(true);
    }

    [EntityInput("HideWorldLayerAndDestroyEntities")]
    private void InputHideWorldLayerAndDestroyEntities(EntityInputData data)
    {
        DestroyLayerEntities();
        SetLayerVisible(false);
    }
}
