using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// Creates and spawns the entities of a spawn group from the world the <see cref="SpawnGroupManager"/> gave it,
/// and loads the groups those entities place as children of it.
/// </summary>
/// <remarks>
/// It never decides which world a group spawns from or loads one; the manager has given the group its world by
/// then. Spawning a whole map only finishes loading that world, as its meshes wait for the entities, see
/// <see cref="WorldLoader.LoadAfterEntities"/>.
/// </remarks>
internal sealed class SpawnGroupLoader
{
    private readonly EntitySystem entitySystem;
    private readonly SpawnGroup group;
    private readonly WorldLoader world;

    private RendererContext RendererContext => entitySystem.RendererContext;

    private SpawnGroupLoader(EntitySystem entitySystem, SpawnGroup group, WorldLoader world)
    {
        this.entitySystem = entitySystem;
        this.group = group;
        this.world = world;
    }

    /// <summary>
    /// Spawns the entity lump the group names, or the lumps the world spawns when it loads.
    /// </summary>
    /// <returns>The entities spawned, none when the world has no such lump.</returns>
    public static List<BaseEntity> Spawn(EntitySystem entitySystem, SpawnGroup group, WorldLoader world)
        => new SpawnGroupLoader(entitySystem, group, world).Spawn();

    private List<BaseEntity> Spawn()
    {
        // Created with the first map, before any of its entities so it owns them all
        entitySystem.SpawnWorld(group.Scene);

        var layerName = group.LayerName ?? "Entities";

        if (group.EntityLumpName == null)
        {
            var mapEntities = world.ListMapEntities();
            group.Entities = world.Entities;

            var spawned = SpawnEntities(mapEntities, layerName);

            // The placing map stores prefab lights once its lump is done
            if (world.OwnsScene)
            {
                group.Scene.LightingInfo.StoreLights(group.Scene.AllNodes.OfType<SceneLight>().ToList());
            }

            world.LoadAfterEntities();

            return spawned;
        }

        // A template with no compiled entities still spawns an empty instance
        if (group.EntityLumpName.Length == 0)
        {
            return [];
        }

        if ((group.EntityLump ?? world.FindEntityLump(group.EntityLumpName)) is not { } lump)
        {
            RendererContext.Logger.LogWarning("Spawn group '{DescriptiveName}' found no entity lump named '{EntityLumpName}' in '{MapName}'", group.DescriptiveName, group.EntityLumpName, world.MapName);
            return [];
        }

        // A stopped world reaches a template's lump again through templates inside it
        if (!world.SpawningLumps.Add(lump.Name))
        {
            RendererContext.Logger.LogWarning("Spawn group '{DescriptiveName}' spawns entity lump '{EntityLumpName}' from inside itself", group.DescriptiveName, lump.Name);
            return [];
        }

        try
        {
            var lumpEntities = world.ListLumpEntities(lump);
            group.Entities = lumpEntities;

            return SpawnEntities(lumpEntities, layerName);
        }
        finally
        {
            world.SpawningLumps.Remove(lump.Name);
        }
    }

    /// <summary>
    /// Spawns the entities as the engine does: every entity is created, then the groups they place finish, then
    /// the group spawns and activates its own entities as one batch.
    /// </summary>
    private List<BaseEntity> SpawnEntities(List<Entity> groupEntities, string layerName)
    {
        static bool IsCubemapOrProbe(string cls)
            => cls == "env_combined_light_probe_volume"
            || cls == "env_light_probe_volume"
            || cls == "env_cubemap_box"
            || cls == "env_cubemap";

        var entitiesReordered = groupEntities
            .Select(entity => (Entity: entity, Classname: entity.GetStringProperty("classname")))
            .Where(x => x.Classname != null)
            .Select(x => (x.Entity, Classname: x.Classname!))
            .OrderByDescending(x => IsCubemapOrProbe(x.Classname));

        var batch = new List<BaseEntity>(groupEntities.Count);
        var placements = new List<(Entity Entity, string Classname)>();

        foreach (var (entity, classname) in entitiesReordered)
        {
            RendererContext.CancellationToken.ThrowIfCancellationRequested();

            try
            {
                var placesMap = IsSpawnGroupPlacement(classname, entity);

                // The game creates no entity for one that places a map, other than the reference of a 3D sky. A
                // stopped world still shows it, as the editor does.
                if ((!placesMap || classname == "skybox_reference" || !entitySystem.Enabled)
                    && CreateEntity(entity, layerName) is { } created)
                {
                    batch.Add(created);
                }

                if (placesMap)
                {
                    placements.Add((entity, classname));
                }
            }
            catch (Exception e)
            {
                throw EntitySystem.EntityException(entity, classname, e);
            }
        }

        // Once every entity is created, as the game spawns the groups placed by this one only once it has
        // created all of its own
        foreach (var (entity, classname) in placements)
        {
            try
            {
                HandlePointPrefab(entity, classname);
            }
            catch (Exception e)
            {
                throw EntitySystem.EntityException(entity, classname, e);
            }
        }

        entitySystem.SpawnEntities(batch);

        world.RecordSpawned(batch);

        // Once the whole group has spawned, so a relation can reach an entity authored after the one it
        // starts at. The groups placed by this one relate their own.
        entitySystem.ToolVisuals?.AddEntityRelations(group.Scene, batch);

        return batch;
    }

    private BaseEntity? CreateEntity(Entity entity, string layerName)
    {
        var disabled = entity.GetBooleanProperty("startdisabled");

        if (!disabled)
        {
            disabled = !entity.GetBooleanProperty("enabled", true);
        }

        if (disabled && layerName == "Entities")
        {
            layerName = WorldLoader.DisabledEntitiesLayerName;
        }

        // Every entity joins the entity system, so it can be named and targeted, and draws itself
        return entitySystem.CreateEntity(entity, layerName, group);
    }

    // Any class flagged ispointprefab places a map too, such as the CS2 team select and team intro stages.
    // A point_prefab with prefab_load_dynamic switched off places nothing.
    private static bool IsSpawnGroupPlacement(string classname, Entity entity)
        => classname == "skybox_reference"
        || (classname == "point_prefab" && entity.GetBooleanProperty("prefab_load_dynamic", defaultValue: true))
        || entity.GetBooleanProperty("ispointprefab");

    /// <summary>
    /// Gets the world group a map placed by an entity loads into: the one its <c>worldgroupid</c> names, else
    /// the one of the group that placed it.
    /// </summary>
    internal static string? GetPlacedWorldGroup(Entity placement, string? parentWorldGroup)
        => placement.GetStringProperty("worldgroupid") is { Length: > 0 } worldGroup ? worldGroup : parentWorldGroup;

    // Loads the map an entity places as a child group of the one placing it, from the entity's keyvalues
    // alone. A 3D sky is no different from a prefab, other than going into a world group of its own, which
    // is drawn as a scene of its own. Compiled maps have prefabs merged in already; the ones left are loaded
    // by the game at runtime.
    private void HandlePointPrefab(Entity placement, string classname)
    {
        if (placement.GetStringProperty("targetmapname") is not { Length: > 0 } targetMapName)
        {
            return;
        }

        var transform = EntityTransformHelper.ToRigidTransformationMatrix(placement) * group.Transform;

        var worldGroup = GetPlacedWorldGroup(placement, group.Scene.WorldGroup);
        var hasOwnScene = worldGroup != group.Scene.WorldGroup;
        var scene = hasOwnScene ? new Scene(RendererContext) { WorldGroup = worldGroup } : group.Scene;

        var child = new SpawnGroup(WorldLoader.GetSpawnGroupMapName(targetMapName), scene, transform)
        {
            DescriptiveName = "point_prefab",
            NameFixup = ChildNameFixup(placement),
            HasOwnScene = hasOwnScene,
        };

        var isSky = hasOwnScene && classname == "skybox_reference";

        if (isSky)
        {
            world.LoadingProgress?.Report("Loading 3D sky…");
        }

        var spawnGroupManager = entitySystem.SpawnGroupManager;
        spawnGroupManager.LoadSpawnGroup(child);
        spawnGroupManager.AddChildSpawnGroup(group, child);
        spawnGroupManager.SynchronouslySpawnGroup(child);

        if (isSky && world.CurrentLoadingPhase is { } phase)
        {
            world.LoadingProgress?.Report(phase);
        }

        if (child.World is not { } childWorld)
        {
            if (hasOwnScene)
            {
                spawnGroupManager.UnloadSpawnGroup(child);
            }

            return;
        }

        if (!hasOwnScene)
        {
            world.Entities.AddRange(childWorld.Entities);
        }
        else if (isSky)
        {
            ShowAsSky(scene, transform.Translation);
        }
    }

    // The viewer shows a sky scene where it appears in the world, with markers shrunk to appear at normal size
    // as the sky camera magnifies the sky
    private void ShowAsSky(Scene skyScene, Vector3 reference)
    {
        // The camera the reference will find, as its sky has spawned before the reference does
        var sky = SkyTransform.FromEntities(reference, SkyboxReference.FindSkyCamera(entitySystem, skyScene.WorldGroup));

        skyScene.ToViewerWorld = sky.SkyToWorld;
        skyScene.MarkerScale = 1f / sky.Scale;

        // The scale is only known once the sky map has loaded, so the markers it shrinks are already
        // placed. Whatever an entity owns it re-places itself from here on.
        foreach (var marker in skyScene.AllNodes)
        {
            if (marker.PlacementScale != 1f)
            {
                marker.Transform = marker.ApplyPlacementScale(marker.Transform);
            }
        }
    }

    // A placed map keeps the fixups of the group that placed it, unless asked to set its names apart with a
    // numbered prefix of its own
    private EntityNameFixup ChildNameFixup(Entity placement)
        => placement.GetBooleanProperty("fixupnames")
            ? group.NameFixup with { Parent = string.Create(CultureInfo.InvariantCulture, $"{entitySystem.SpawnGroupManager.NextNameFixupIndex()}d_{group.NameFixup.Parent}") }
            : group.NameFixup;
}
