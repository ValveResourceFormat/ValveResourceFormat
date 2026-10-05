using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_spawngroup_load_unload</c>. Loads another map into the running one on <c>StartSpawnGroupLoad</c>
/// and unloads it on <c>StartSpawnGroupUnload</c>.
/// </summary>
/// <remarks>
/// The loaded map is aligned by landmark origin only, with no rotation. The load finishes within the input
/// instead of in the background.
/// </remarks>
public sealed class InfoSpawnGroupLoadUnload : BaseEntity
{
    /// <summary>Gets the map to load, such as <c>stages/lms_stage1</c>.</summary>
    public string MapName { get; private set; } = string.Empty;

    /// <summary>Gets the name of the <c>info_spawngroup_landmark</c> present in both maps.</summary>
    public string Landmark { get; private set; } = string.Empty;

    /// <summary>Gets the loaded spawn group, or <see langword="null"/> when none is loaded.</summary>
    public SpawnGroup? SpawnGroup { get; private set; }

    /// <summary>Initializes an <c>info_spawngroup_load_unload</c> from its keyvalues.</summary>
    public InfoSpawnGroupLoadUnload(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        MapName = KeyValues.GetStringProperty("mapname") ?? string.Empty;
        Landmark = KeyValues.GetStringProperty("landmark") ?? string.Empty;

        if (MapName.Length == 0)
        {
            EntitySystem.Logger.LogWarning("info_spawngroup_load_unload '{TargetName}' names no map", TargetName);
        }
    }

    [EntityInput("StartSpawnGroupLoad")]
    private void InputStartSpawnGroupLoad(EntityInputData data)
    {
        if (SpawnGroup != null || MapName.Length == 0)
        {
            return;
        }

        // The engine will not place a group without a landmark
        if (Landmark.Length == 0)
        {
            EntitySystem.Logger.LogWarning("info_spawngroup_load_unload '{TargetName}' names no landmark, not loading '{MapName}'", TargetName, MapName);
            return;
        }

        if (FindLandmark() is not { } landmark)
        {
            EntitySystem.Logger.LogWarning("info_spawngroup_load_unload '{TargetName}' found no landmark named '{Landmark}'", TargetName, Landmark);
            return;
        }

        EntitySystem.TriggerOutput(this, "OnSpawnGroupLoadStarted", data.Activator);

        SpawnGroup = WorldLoader.LoadSpawnGroup(EntitySystem.RendererContext, EntitySystem, MapName, Landmark, landmark.Transform.Translation);

        if (SpawnGroup == null)
        {
            return;
        }

        EntitySystem.AddSpawnGroup(SpawnGroup);
        EntitySystem.TriggerOutput(this, "OnSpawnGroupLoadFinished", data.Activator);
    }

    [EntityInput("StartSpawnGroupUnload")]
    private void InputStartSpawnGroupUnload(EntityInputData data)
    {
        if (SpawnGroup is not { } group)
        {
            return;
        }

        EntitySystem.TriggerOutput(this, "OnSpawnGroupUnloadStarted", data.Activator);

        SpawnGroup = null;
        EntitySystem.RemoveSpawnGroup(group);

        EntitySystem.TriggerOutput(this, "OnSpawnGroupUnloadFinished", data.Activator);
    }

    // The first entity of that name, which has to be a landmark
    private BaseEntity? FindLandmark()
        => EntitySystem.FindByTargetName(Landmark) is { Classname: "info_spawngroup_landmark" } landmark ? landmark : null;
}
