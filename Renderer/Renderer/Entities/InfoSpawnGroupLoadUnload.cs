using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_spawngroup_load_unload</c>. Loads another map into the running one on <c>StartSpawnGroupLoad</c>
/// and takes it out again on <c>StartSpawnGroupUnload</c>, which is how a map swaps its stages in and out.
/// </summary>
/// <remarks>
/// The loaded map is placed so its <c>info_spawngroup_landmark</c> lands on the one of the same name in
/// this map, by origin alone, as the engine does. It is loaded as a spawn group of its own, see
/// <see cref="SpawnGroup"/>. The engine loads it in the background; here the load finishes within the input.
/// </remarks>
public sealed class InfoSpawnGroupLoadUnload : BaseEntity
{
    /// <summary>Gets the map this entity loads, such as <c>stages/lms_stage1</c>.</summary>
    public string MapName { get; private set; } = string.Empty;

    /// <summary>Gets the name of the landmark that lines the loaded map up with this one.</summary>
    public string Landmark { get; private set; } = string.Empty;

    /// <summary>Gets the spawn group this entity loaded, or <see langword="null"/> while none is.</summary>
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

    /// <summary>
    /// Finds this map's <c>info_spawngroup_landmark</c> of the landmark name. Compiled names carry a
    /// <c>[PR#]</c> prefix the keyvalue leaves out.
    /// </summary>
    private BaseEntity? FindLandmark()
    {
        foreach (var pattern in (ReadOnlySpan<string>)[Landmark, "[PR#]" + Landmark])
        {
            foreach (var candidate in EntitySystem.FindAllByTargetName(pattern, Scene))
            {
                if (candidate.Classname == "info_spawngroup_landmark")
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
