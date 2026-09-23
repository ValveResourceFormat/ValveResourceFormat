using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Constructs an entity of one classname.
/// </summary>
/// <returns>The constructed entity, before <see cref="BaseEntity.Spawn"/> has run.</returns>
public delegate BaseEntity EntityCreator(EntitySystem system, EntitySpawnInfo spawnInfo);

/// <summary>
/// Turns a classname into a live entity. Source's entity factory dictionary: every simulated classname
/// registers here, and anything absent from the table is not one the entity system implements.
/// </summary>
/// <remarks>
/// The table is filled in statically rather than by scanning types, to stay trim-safe and AOT-compatible.
/// </remarks>
public static class EntityFactory
{
    // Concurrent for the same reason the input tables are: the registrations themselves all happen in the
    // static constructor, but the map loads that read this run on several threads at once, and nothing in
    // the type stops a caller registering a classname of its own later
    private static readonly ConcurrentDictionary<string, EntityCreator> Creators = new(StringComparer.OrdinalIgnoreCase);

    static EntityFactory()
    {
        // Not registered to a classname, but they still answer the inputs every entity has
        EntityInputTable.Bind<GenericEntity>();
        EntityInputTable.Bind<GenericModelEntity>();

        Register<WorldEntity>("worldspawn", static (system, spawnInfo) => new WorldEntity(system, spawnInfo));
        Register<InfoWorldLayer>("info_world_layer", static (system, spawnInfo) => new InfoWorldLayer(system, spawnInfo));

        Register<FuncBrush>("func_brush", static (system, spawnInfo) => new FuncBrush(system, spawnInfo));
        Register<FuncCombineBarrier>("func_combine_barrier", static (system, spawnInfo) => new FuncCombineBarrier(system, spawnInfo));
        Register<FuncButton>("func_button", static (system, spawnInfo) => new FuncButton(system, spawnInfo));
        Register<FuncDoor>("func_door", static (system, spawnInfo) => new FuncDoor(system, spawnInfo));
        Register<FuncDoorRotating>("func_door_rotating", static (system, spawnInfo) => new FuncDoorRotating(system, spawnInfo));
        Register<FuncDoor>("func_movelinear", static (system, spawnInfo) => new FuncDoor(system, spawnInfo));
        Register<FuncRotating>("func_rotating", static (system, spawnInfo) => new FuncRotating(system, spawnInfo));
        Register<PropDoorRotating>("prop_door_rotating", static (system, spawnInfo) => new PropDoorRotating(system, spawnInfo));
        Register<PropDoorRotating>("prop_door_rotating_physics", static (system, spawnInfo) => new PropDoorRotating(system, spawnInfo));
        Register<PropDynamic>("prop_dynamic", static (system, spawnInfo) => new PropDynamic(system, spawnInfo));
        Register<PropDynamic>("prop_dynamic_override", static (system, spawnInfo) => new PropDynamic(system, spawnInfo));
        Register<FuncBreakable>("func_breakable", static (system, spawnInfo) => new FuncBreakable(system, spawnInfo));
        Register<XenFloraAnimatedMover>("xen_flora_animatedmover", static (system, spawnInfo) => new XenFloraAnimatedMover(system, spawnInfo));
        Register<TriggerTeleport>("trigger_teleport", static (system, spawnInfo) => new TriggerTeleport(system, spawnInfo));

        // lights
        Register<LightEntity>("light_barn", static (system, spawnInfo) => new LightEntity(system, spawnInfo));
        Register<LightEntity>("light_environment", static (system, spawnInfo) => new LightEntity(system, spawnInfo));
        Register<LightEntity>("light_omni", static (system, spawnInfo) => new LightEntity(system, spawnInfo));
        Register<LightEntity>("light_omni2", static (system, spawnInfo) => new LightEntity(system, spawnInfo));
        Register<LightEntity>("light_ortho", static (system, spawnInfo) => new LightEntity(system, spawnInfo));
        Register<LightEntity>("light_rect", static (system, spawnInfo) => new LightEntity(system, spawnInfo));
        Register<LightEntity>("light_spot", static (system, spawnInfo) => new LightEntity(system, spawnInfo));

        // environment
        Register<EnvCubemap>("env_cubemap", static (system, spawnInfo) => new EnvCubemap(system, spawnInfo, isSphere: true));
        Register<EnvCubemap>("env_cubemap_box", static (system, spawnInfo) => new EnvCubemap(system, spawnInfo, isSphere: false));
        Register<EnvCubemapFog>("env_cubemap_fog", static (system, spawnInfo) => new EnvCubemapFog(system, spawnInfo));
        Register<EnvLightProbeVolume>("env_combined_light_probe_volume", static (system, spawnInfo) => new EnvLightProbeVolume(system, spawnInfo, bakesCubemap: true));
        Register<EnvLightProbeVolume>("env_light_probe_volume", static (system, spawnInfo) => new EnvLightProbeVolume(system, spawnInfo, bakesCubemap: false));
        Register<EnvGradientFog>("env_gradient_fog", static (system, spawnInfo) => new EnvGradientFog(system, spawnInfo));
        Register<EnvSky>("env_global_light", static (system, spawnInfo) => new EnvSky(system, spawnInfo, isGlobalLight: true));
        Register<EnvSky>("env_sky", static (system, spawnInfo) => new EnvSky(system, spawnInfo, isGlobalLight: false));
        Register<EnvTonemapController>("env_tonemap_controller", static (system, spawnInfo) => new EnvTonemapController(system, spawnInfo));
        Register<InfoMapParameters>("info_map_parameters", static (system, spawnInfo) => new InfoMapParameters(system, spawnInfo));

        // particles
        Register<EnvExplosion>("env_explosion", static (system, spawnInfo) => new EnvExplosion(system, spawnInfo));
        Register<InfoParticleSystem>("info_particle_system", static (system, spawnInfo) => new InfoParticleSystem(system, spawnInfo));
        Register<InfoParticleSystem>("dota_world_particle_system", static (system, spawnInfo) => new InfoParticleSystem(system, spawnInfo));
        Register<EnvParticleGlow>("env_particle_glow", static (system, spawnInfo) => new EnvParticleGlow(system, spawnInfo));

        // models with an ambient effect
        Register<AmbientEffectEntity>("dcg_game_board_attachment", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));
        Register<AmbientEffectEntity>("ent_dota_fountain", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));
        Register<AmbientEffectEntity>("ent_dota_tree", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));
        Register<AmbientEffectEntity>("npc_dota_barracks", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));
        Register<AmbientEffectEntity>("npc_dota_building", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));
        Register<AmbientEffectEntity>("npc_dota_fort", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));
        Register<AmbientEffectEntity>("npc_dota_lotus_pool", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));
        Register<AmbientEffectEntity>("npc_dota_mango_tree", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));
        Register<AmbientEffectEntity>("npc_dota_tower", static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo));

        // A rope's effect_name is the cable effect, which drawn without the rope's own snapshot is an
        // editor placeholder at the world origin, so ropes must never be treated as plain particle entities
        Register<PathParticleRopeEntity>("path_particle_rope", static (system, spawnInfo) => new PathParticleRopeEntity(system, spawnInfo));
        Register<PathParticleRopeEntity>("path_particle_rope_clientside", static (system, spawnInfo) => new PathParticleRopeEntity(system, spawnInfo));
        Register<PathParticleRopeEntity>("citadel_zipline_path", static (system, spawnInfo) => new PathParticleRopeEntity(system, spawnInfo));
        Register<PostProcessingVolume>("post_processing_volume", static (system, spawnInfo) => new PostProcessingVolume(system, spawnInfo));

        // cameras and spawn points
        Register<PointCamera>("point_camera", static (system, spawnInfo) => new PointCamera(system, spawnInfo));
        Register<PointCamera>("point_camera_vertical_fov", static (system, spawnInfo) => new PointCamera(system, spawnInfo));
        Register<PointCamera>("point_devshot_camera", static (system, spawnInfo) => new PointCamera(system, spawnInfo));
        Register<SkyCamera>("sky_camera", static (system, spawnInfo) => new SkyCamera(system, spawnInfo));
        Register<SpawnPoint>("info_player_counterterrorist", static (system, spawnInfo) => new SpawnPoint(system, spawnInfo));
        Register<SpawnPoint>("info_player_start", static (system, spawnInfo) => new SpawnPoint(system, spawnInfo));
        Register<SpawnPoint>("info_player_start_badguys", static (system, spawnInfo) => new SpawnPoint(system, spawnInfo));
        Register<SpawnPoint>("info_player_start_goodguys", static (system, spawnInfo) => new SpawnPoint(system, spawnInfo));
        Register<SpawnPoint>("info_player_terrorist", static (system, spawnInfo) => new SpawnPoint(system, spawnInfo));
        Register<SpawnPoint>("info_team_spawn", static (system, spawnInfo) => new SpawnPoint(system, spawnInfo));
        Register<SpawnPoint>("team_select", static (system, spawnInfo) => new SpawnPoint(system, spawnInfo));

        // logic
        Register<LogicAuto>("logic_auto", static (system, spawnInfo) => new LogicAuto(system, spawnInfo));
        Register<LogicCase>("logic_case", static (system, spawnInfo) => new LogicCase(system, spawnInfo));
        Register<LogicRelay>("logic_relay", static (system, spawnInfo) => new LogicRelay(system, spawnInfo));
        Register<LogicTimer>("logic_timer", static (system, spawnInfo) => new LogicTimer(system, spawnInfo));
        Register<MathCounter>("math_counter", static (system, spawnInfo) => new MathCounter(system, spawnInfo));
        Register<FilterActivatorModel>("filter_activator_model", static (system, spawnInfo) => new FilterActivatorModel(system, spawnInfo));

        // sounds
        Register<EnvSoundscape>("env_soundscape", static (system, spawnInfo) => new EnvSoundscape(system, spawnInfo));
        Register<PointSoundEvent>("point_soundevent", static (system, spawnInfo) => new PointSoundEvent(system, spawnInfo));
        Register<PointSoundEvent>("snd_event_point", static (system, spawnInfo) => new PointSoundEvent(system, spawnInfo));
        Register<EnvSoundscape>("snd_soundscape", static (system, spawnInfo) => new EnvSoundscape(system, spawnInfo));
        Register<AmbientGeneric>("ambient_generic", static (system, spawnInfo) => new AmbientGeneric(system, spawnInfo));
    }

    /// <summary>
    /// Registers a classname the entity system should simulate, and builds the entity class's table of
    /// <see cref="EntityInputAttribute"/> handlers. Safe to call while maps are loading, though the static
    /// constructor below is where the entity system's own classnames are declared.
    /// </summary>
    /// <typeparam name="T">The entity class the classname spawns.</typeparam>
    /// <param name="classname">The classname to link, matched case-insensitively.</param>
    /// <param name="creator">Constructs the entity.</param>
    public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] T>(
        string classname, EntityCreator creator)
        where T : BaseEntity
    {
        Creators[classname] = creator;
        EntityInputTable.Bind<T>();
    }

    /// <summary>
    /// Creates and spawns the entity for a classname. The entity is fully set up when this returns, but
    /// is not in the world yet; <see cref="EntitySystem.CreateEntity"/> is what puts it there. A classname
    /// that is not implemented spawns a <see cref="GenericModelEntity"/> when it has a model, and a
    /// <see cref="GenericEntity"/> when it does not.
    /// </summary>
    /// <returns>The spawned entity, or <see langword="null"/> when the keyvalues name no classname.</returns>
    public static BaseEntity? Create(EntitySystem system, EntitySpawnInfo spawnInfo)
    {
        var classname = spawnInfo.Data.GetStringProperty("classname");

        if (classname == null)
        {
            return null;
        }

        BaseEntity entity;

        if (Creators.TryGetValue(classname, out var creator))
        {
            entity = creator(system, spawnInfo);
        }
        else if (string.IsNullOrEmpty(spawnInfo.Data.GetStringProperty("model")))
        {
            entity = new GenericEntity(system, spawnInfo);
        }
        else
        {
            entity = new GenericModelEntity(system, spawnInfo);
        }

        entity.Spawn();

        return entity;
    }
}
