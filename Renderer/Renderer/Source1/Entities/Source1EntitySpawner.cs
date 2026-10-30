using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using Entity = ValveResourceFormat.ResourceTypes.EntityLump.Entity;

namespace ValveResourceFormat.Renderer.Source1.Entities;

/// <summary>
/// Spawns the converted entities of a Source 1 map into a scene, through the entity system a compiled Source 2
/// map spawns its entities with, so they get the same entity classes, lights and entity I/O.
/// </summary>
public static class Source1EntitySpawner
{
    private const string EntitiesLayerName = "Entities";

    /// <summary>
    /// Creates and spawns entities as the entity lump of a map, then stores the lights they made.
    /// </summary>
    /// <param name="entitySystem">The entity system of the view. Its <see cref="EntitySystem.ToolVisuals"/> are turned off.</param>
    /// <param name="scene">The scene the entities draw into.</param>
    /// <param name="mapName">The name of the map.</param>
    /// <param name="entities">The entities, as converted by <c>Source1EntityConverter</c>.</param>
    /// <param name="lightFocus">The position the lights nearest to are kept when the map has more than the light store holds.</param>
    /// <returns>The entities that were created, in order.</returns>
    public static List<BaseEntity> Spawn(EntitySystem entitySystem, Scene scene, string mapName, IReadOnlyList<Entity> entities, Vector3 lightFocus = default)
    {
        ArgumentNullException.ThrowIfNull(entitySystem);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(mapName);
        ArgumentNullException.ThrowIfNull(entities);

        scene.RendererContext.Source1Particles?.SetMap(mapName);

        // Building a tool stand-in for every entity of the map costs more load time than the view gains
        entitySystem.ToolVisuals = null;
        entitySystem.SpawnWorld(scene);

        var group = new SpawnGroup(mapName, scene, Matrix4x4.Identity)
        {
            DescriptiveName = "source1_map",
        };

        group.IsLoaded = true;

        var batch = new List<BaseEntity>(entities.Count);

        foreach (var entity in entities)
        {
            scene.RendererContext.CancellationToken.ThrowIfCancellationRequested();

            DetachSource1Model(entity);
            SetTonemapDefaults(entity);

            var entityLayerName = entity.GetBooleanProperty("startdisabled") ? WorldLoader.DisabledEntitiesLayerName : EntitiesLayerName;

            try
            {
                if (entitySystem.CreateEntity(entity, entityLayerName, group) is { } created)
                {
                    batch.Add(created);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                entitySystem.Logger.LogWarning(e, "Failed to create entity '{Classname}'", entity.GetStringProperty("classname"));
            }
        }

        entitySystem.SpawnEntities(batch);

        group.Entities = entities;
        group.IsSpawned = true;

        scene.LightingInfo.StoreLights(SelectLights(scene, lightFocus));

        return batch;
    }

    /// <summary>
    /// Sets the fog the 3D sky is drawn with, from the keyvalues of the <c>sky_camera</c>. Its distances are in
    /// world units, like the fog of the map.
    /// </summary>
    /// <param name="skyScene">The scene of the 3D sky.</param>
    /// <param name="entities">The entities, as converted by <c>Source1EntityConverter</c>.</param>
    public static void SetSkyFog(Scene skyScene, IReadOnlyList<Entity> entities)
    {
        ArgumentNullException.ThrowIfNull(skyScene);
        ArgumentNullException.ThrowIfNull(entities);

        var skyCamera = entities.FirstOrDefault(static entity => entity.GetStringProperty("classname") == "sky_camera");

        if (skyCamera == null || !skyCamera.GetBooleanProperty("fogenable"))
        {
            return;
        }

        var color = skyCamera.GetVector3Property("fogcolor", new Vector3(255f)) / 255f;
        var secondaryColor = skyCamera.GetVector3Property("fogcolor2", new Vector3(255f)) / 255f;

        skyScene.FogInfo.LinearFog = new LinearFog(
            ColorSpace.SrgbGammaToLinear(color),
            skyCamera.GetFloatProperty("fogstart", 500f),
            skyCamera.GetFloatProperty("fogend", 2000f),
            MathUtils.Saturate(skyCamera.GetFloatProperty("fogmaxdensity", 1f)),
            1f)
        {
            SecondaryColor = ColorSpace.SrgbGammaToLinear(secondaryColor),
            BlendDirection = EnvFogController.GetBlendDirection(skyCamera, skyCamera.GetVector3Property("angles")),
        };
        skyScene.FogInfo.ObjectsUseLinearFog = true;
    }

    // A Source 1 tonemap controller sets its exposure range through inputs, so without keys it would clamp the exposure to zero
    private static void SetTonemapDefaults(Entity entity)
    {
        if (entity.GetStringProperty("classname") != "env_tonemap_controller" || entity.ContainsKey("maxexposure"))
        {
            return;
        }

        entity["minexposure"] = 0.5f;
        entity["maxexposure"] = 2f;
        entity["rate"] = 1f;
    }

    // The model, sprite and brush geometry of Source 1 entities is drawn by the caller, which the entity classes cannot load
    private static void DetachSource1Model(Entity entity)
    {
        var model = entity.GetStringProperty("model");

        if (string.IsNullOrEmpty(model))
        {
            return;
        }

        var isSource1Model = model[0] == '*'
            || model.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
            || model.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase)
            || model.EndsWith(".spr", StringComparison.OrdinalIgnoreCase);

        if (!isSource1Model)
        {
            return;
        }

        entity["source1_model"] = model;
        entity["model"] = string.Empty;
    }

    // The light store holds a fixed number of lights, so a map with more keeps the ones nearest to the focus
    private static List<SceneLight> SelectLights(Scene scene, Vector3 focus)
    {
        var lights = scene.AllNodes.OfType<SceneLight>().ToList();

        if (lights.Count < LightingConstants.MAX_LIGHTS)
        {
            return lights;
        }

        var selected = lights
            .Where(static light => light.Entity == SceneLight.EntityType.Environment)
            .ToList();

        var nearest = lights
            .Where(static light => light.Entity != SceneLight.EntityType.Environment)
            .OrderBy(light => Vector3.DistanceSquared(light.Position, focus))
            .Take(LightingConstants.MAX_LIGHTS - selected.Count);

        selected.AddRange(nearest);

        return selected;
    }
}
