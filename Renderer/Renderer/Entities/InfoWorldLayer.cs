using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_world_layer</c>. Shows and hides one world layer, and spawns and destroys the entities
/// compiled into the layer's own entity lump, by entity I/O.
/// </summary>
/// <remarks>
/// Entities spawned by an input after the map has loaded are not bound to the scene's cubemaps and light
/// probes, which are only assigned when the scene is first initialized.
/// </remarks>
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

    /// <summary>Gets the entities spawned from the layer's entity lump, or <see langword="null"/> when they are not spawned.</summary>
    public IReadOnlyList<BaseEntity>? LayerEntities { get; private set; }

    /// <summary>Spawns the layer's entity lump, set by the map that placed this entity.</summary>
    internal Func<InfoWorldLayer, IReadOnlyList<BaseEntity>?>? LayerSpawner { get; set; }

    /// <summary>Initializes an <c>info_world_layer</c> from its keyvalues.</summary>
    public InfoWorldLayer(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Shows or hides the controlled layer.</summary>
    public void SetLayerVisible(bool visible)
    {
        if (string.IsNullOrEmpty(WorldLayerName))
        {
            return;
        }

        if (visible)
        {
            Scene.ActivateLayer(WorldLayerName);
        }
        else
        {
            Scene.DeactivateLayer(WorldLayerName);
        }
    }

    /// <summary>
    /// Spawns the entities of the layer's entity lump, unless they already are, and fires
    /// <c>OnEntitiesSpawned</c>.
    /// </summary>
    /// <returns>Whether any were spawned.</returns>
    internal bool SpawnLayerEntities()
    {
        if (LayerEntities != null || LayerSpawner == null || string.IsNullOrEmpty(WorldName) || string.IsNullOrEmpty(WorldLayerName))
        {
            return false;
        }

        LayerEntities = LayerSpawner(this);

        if (LayerEntities == null)
        {
            return false;
        }

        EntitySystem.TriggerOutput(this, "OnEntitiesSpawned", this);
        return true;
    }

    private void DestroyLayerEntities()
    {
        if (LayerEntities is not { } layerEntities)
        {
            return;
        }

        LayerEntities = null;

        foreach (var entity in layerEntities)
        {
            EntitySystem.Remove(entity);
        }
    }

    // Spawned by an input, so the map has long been activated and the new entities need it on their own
    private void SpawnLayerEntitiesNow()
    {
        if (SpawnLayerEntities())
        {
            EntitySystem.Activate();
        }
    }

    /// <inheritdoc/>
    protected override void OnRemove() => DestroyLayerEntities();

    [EntityInput("ShowWorldLayer")] private void InputShowWorldLayer(EntityInputData data) => SetLayerVisible(true);

    [EntityInput("HideWorldLayer")] private void InputHideWorldLayer(EntityInputData data) => SetLayerVisible(false);

    [EntityInput("SpawnEntities")] private void InputSpawnEntities(EntityInputData data) => SpawnLayerEntitiesNow();

    [EntityInput("DestroyEntities")] private void InputDestroyEntities(EntityInputData data) => DestroyLayerEntities();

    [EntityInput("ShowWorldLayerAndSpawnEntities")]
    private void InputShowWorldLayerAndSpawnEntities(EntityInputData data)
    {
        SpawnLayerEntitiesNow();
        SetLayerVisible(true);
    }

    [EntityInput("HideWorldLayerAndDestroyEntities")]
    private void InputHideWorldLayerAndDestroyEntities(EntityInputData data)
    {
        DestroyLayerEntities();
        SetLayerVisible(false);
    }
}
