using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>info_world_layer</c>. Shows and hides one world layer by entity I/O.</summary>
public sealed class InfoWorldLayer : BaseEntity
{
    /// <summary>Spawn flags for world layers.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>The layer is shown when the map loads.</summary>
        VisibleOnSpawn = 1,
    }

    /// <summary>Gets the name of the world layer this entity controls.</summary>
    public string? WorldLayerName { get; private set; }

    /// <summary>Gets whether the controlled layer is shown when the map loads.</summary>
    public bool IsVisibleOnSpawn => HasSpawnFlags(SpawnFlag.VisibleOnSpawn);

    /// <summary>Initializes an <c>info_world_layer</c> from its keyvalues.</summary>
    public InfoWorldLayer(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        WorldLayerName = KeyValues.GetStringProperty("layername");
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

    [EntityInput("ShowWorldLayer")] private void InputShowWorldLayer(EntityInputData data) => SetLayerVisible(true);

    [EntityInput("HideWorldLayer")] private void InputHideWorldLayer(EntityInputData data) => SetLayerVisible(false);

    [EntityInput("ShowWorldLayerAndSpawnEntities")]
    private void InputShowWorldLayerAndSpawnEntities(EntityInputData data) => SetLayerVisible(true);

    // [EntityInput("SpawnEntities")]
    // [EntityInput("DestryEntities")] // "Destry" is the fgd's own spelling
}
