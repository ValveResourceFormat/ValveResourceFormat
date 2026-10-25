using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using WorldResource = ValveResourceFormat.ResourceTypes.World;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>info_spawngroup_load_unload</c>. Loads another map into the running one as a spawn group on
/// <c>StartSpawnGroupLoad</c> and unloads it on <c>StartSpawnGroupUnload</c>.
/// </summary>
/// <remarks>
/// The group is found by the map it loads, so a map loads once however many of these name it, and any of them
/// unloads or activates it. The loaded map is aligned by landmark origin only, with no rotation. The load
/// finishes within the input instead of in the background.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CInfoSpawnGroupLoadUnload">CInfoSpawnGroupLoadUnload</seealso>
public sealed class InfoSpawnGroupLoadUnload : BaseEntity
{
    /// <summary>Gets the map to load, such as <c>stages/lms_stage1</c>.</summary>
    public string MapName { get; private set; } = string.Empty;

    /// <summary>Gets the name of the <c>info_spawngroup_landmark</c> present in both maps.</summary>
    public string Landmark { get; private set; } = string.Empty;

    /// <summary>Gets the spawn group loaded from <see cref="MapName"/>, or <see langword="null"/> when none is loaded.</summary>
    public SpawnGroup? LoadedSpawnGroup => MapName.Length == 0 ? null : EntitySystem.SpawnGroupManager.FindSpawnGroupByWorldName(WorldLoader.GetSpawnGroupMapName(MapName));

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
        if (MapName.Length == 0 || LoadedSpawnGroup != null)
        {
            return;
        }

        // A group cannot be placed without a landmark
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

        EntitySystem.TriggerOutput(this, "OnSpawnGroupLoadStarted", this);

        if (LoadSpawnGroup(landmark.Transform.Translation) is null)
        {
            return;
        }

        EntitySystem.TriggerOutput(this, "OnSpawnGroupLoadFinished", this);
    }

    [EntityInput("StartSpawnGroupUnload")]
    private void InputStartSpawnGroupUnload(EntityInputData data)
    {
        if (LoadedSpawnGroup is not { } group)
        {
            return;
        }

        EntitySystem.TriggerOutput(this, "OnSpawnGroupUnloadStarted", this);
        EntitySystem.SpawnGroupManager.UnloadSpawnGroup(group);
        EntitySystem.TriggerOutput(this, "OnSpawnGroupUnloadFinished", this);
    }

    [EntityInput("ActivateSpawnGroup")]
    private void InputActivateSpawnGroup(EntityInputData data)
    {
        if (LoadedSpawnGroup is { } group)
        {
            EntitySystem.SpawnGroupManager.PerformActivateSpawnGroup(group);
        }
    }

    [EntityInput("SetSpawnGroup")] private void InputSetSpawnGroup(EntityInputData data) => MapName = data.Parameter ?? string.Empty;

    /// <summary>
    /// Loads the map as a spawn group, moved so the landmark of the loaded map lands on
    /// <paramref name="landmarkOrigin"/>. Landmarks are aligned by origin only, ignoring angles.
    /// </summary>
    private SpawnGroup? LoadSpawnGroup(Vector3 landmarkOrigin)
    {
        var fileLoader = EntitySystem.RendererContext.FileLoader;
        var mapName = WorldLoader.GetSpawnGroupMapName(MapName);
        var mapPackage = $"{mapName}.vpk";

        if (!fileLoader.TryMountMapPackage(mapPackage))
        {
            return null;
        }

        var spawnGroupManager = EntitySystem.SpawnGroupManager;
        SpawnGroup? group = null;

        try
        {
            var transform = Matrix4x4.Identity;

            if (FindLoadedLandmarkOrigin($"{mapName}.vmap") is { } groupLandmarkOrigin)
            {
                transform = Matrix4x4.CreateTranslation(landmarkOrigin - groupLandmarkOrigin);
            }

            // Owns the scene and the package once loaded, which unloading it releases
            group = new SpawnGroup(mapName, new Scene(EntitySystem.RendererContext), transform)
            {
                HasOwnScene = true,
                MountedMapPackage = mapPackage,
            };

            spawnGroupManager.LoadSpawnGroup(group);
            spawnGroupManager.SynchronouslySpawnGroup(group);
        }
        catch
        {
            if (group == null)
            {
                fileLoader.UnmountMapPackage(mapPackage);
            }
            else
            {
                spawnGroupManager.UnloadSpawnGroup(group);
            }

            throw;
        }

        if (group.World == null)
        {
            spawnGroupManager.UnloadSpawnGroup(group);
            return null;
        }

        group.Scene.Initialize();

        return group;
    }

    // By the names the group spawns its entities with, which a group loaded by landmark leaves without fixups
    private Vector3? FindLoadedLandmarkOrigin(string mapResourceName)
    {
        var fileLoader = EntitySystem.RendererContext.FileLoader;

        if (fileLoader.LoadFileCompiled(WorldLoader.GetWorldNameFromMap(mapResourceName))?.DataBlock is not WorldResource world)
        {
            return null;
        }

        foreach (var lumpName in world.GetEntityLumpNames())
        {
            if (lumpName == null || fileLoader.LoadFileCompiled(lumpName)?.DataBlock is not EntityLump lump)
            {
                continue;
            }

            foreach (var (entity, parentTransform, _) in EntityLumpTraversal.EnumerateEntities(lump, fileLoader, Matrix4x4.Identity))
            {
                if (string.Equals(entity.FriendlyTargetName, Landmark, StringComparison.OrdinalIgnoreCase))
                {
                    return Vector3.Transform(entity.GetVector3Property("origin"), parentTransform);
                }
            }
        }

        EntitySystem.Logger.LogWarning("Found no landmark named '{Landmark}' in '{Map}'", Landmark, mapResourceName);
        return null;
    }

    // The first entity of that name, which has to be a landmark
    private BaseEntity? FindLandmark()
        => EntitySystem.FindByTargetName(Landmark) is { Classname: "info_spawngroup_landmark" } landmark ? landmark : null;
}
