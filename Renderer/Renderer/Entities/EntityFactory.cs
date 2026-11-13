using System.Collections.Frozen;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Constructs an entity of one classname.
/// </summary>
/// <returns>The constructed entity, before <see cref="BaseEntity.Spawn"/> has run.</returns>
public delegate BaseEntity EntityCreator(EntitySystem system, EntitySpawnInfo spawnInfo);

/// <summary>
/// Creates entities from classnames. Classnames absent from the table are not implemented.
/// </summary>
/// <remarks>
/// Filled in statically rather than by scanning types, to stay trim-safe and AOT-compatible.
/// </remarks>
public static class EntityFactory
{
    private static readonly FrozenDictionary<string, EntityCreator> Creators = new Dictionary<string, EntityCreator>(StringComparer.OrdinalIgnoreCase)
    {
        ["info_world_layer"] = static (system, spawnInfo) => new InfoWorldLayer(system, spawnInfo),
        ["info_visibility_box"] = static (system, spawnInfo) => new InfoVisibilityBox(system, spawnInfo),
        ["info_dynamic_shadow_hint"] = static (system, spawnInfo) => new InfoDynamicShadowHint(system, spawnInfo),
        ["info_dynamic_shadow_hint_box"] = static (system, spawnInfo) => new InfoDynamicShadowHint(system, spawnInfo),

        ["func_brush"] = static (system, spawnInfo) => new FuncBrush(system, spawnInfo),
        ["func_water"] = static (system, spawnInfo) => new FuncWater(system, spawnInfo),
        ["func_combine_barrier"] = static (system, spawnInfo) => new FuncCombineBarrier(system, spawnInfo),
        ["func_button"] = static (system, spawnInfo) => new FuncButton(system, spawnInfo),
        ["func_physical_button"] = static (system, spawnInfo) => new FuncPhysicalButton(system, spawnInfo),
        ["func_door"] = static (system, spawnInfo) => new FuncDoor(system, spawnInfo),
        ["func_door_rotating"] = static (system, spawnInfo) => new FuncDoorRotating(system, spawnInfo),
        ["func_movelinear"] = static (system, spawnInfo) => new FuncMoveLinear(system, spawnInfo),
        ["func_rotating"] = static (system, spawnInfo) => new FuncRotating(system, spawnInfo),
        ["func_tracktrain"] = static (system, spawnInfo) => new FuncTrackTrain(system, spawnInfo),
        ["path_track"] = static (system, spawnInfo) => new PathTrack(system, spawnInfo),
        ["prop_door_rotating"] = static (system, spawnInfo) => new PropDoorRotating(system, spawnInfo),
        ["prop_door_rotating_physics"] = static (system, spawnInfo) => new PropDoorRotating(system, spawnInfo),
        ["prop_dynamic"] = static (system, spawnInfo) => new PropDynamic(system, spawnInfo),
        ["prop_dynamic_override"] = static (system, spawnInfo) => new PropDynamic(system, spawnInfo),
        ["prop_handpose"] = static (system, spawnInfo) => new GenericEntity(system, spawnInfo),
        ["npc_vr_citizen_female"] = static (system, spawnInfo) => new NpcVrCitizen(system, spawnInfo),
        ["npc_vr_citizen_male"] = static (system, spawnInfo) => new NpcVrCitizen(system, spawnInfo),
        ["npc_boss_tier3"] = static (system, spawnInfo) => new NpcBossTier3(system, spawnInfo),
        ["func_breakable"] = static (system, spawnInfo) => new FuncBreakable(system, spawnInfo),
        ["func_shatterglass"] = static (system, spawnInfo) => new FuncShatterglass(system, spawnInfo),
        ["xen_flora_animatedmover"] = static (system, spawnInfo) => new XenFloraAnimatedMover(system, spawnInfo),
        ["texture_based_animatable"] = static (system, spawnInfo) => new TextureBasedAnimatable(system, spawnInfo),
        ["hl_vr_texture_based_animatable"] = static (system, spawnInfo) => new TextureBasedAnimatable(system, spawnInfo),
        ["trigger_multiple"] = static (system, spawnInfo) => new TriggerMultiple(system, spawnInfo),
        ["trigger_once"] = static (system, spawnInfo) => new TriggerOnce(system, spawnInfo),
        ["trigger_hurt"] = static (system, spawnInfo) => new TriggerHurt(system, spawnInfo),
        ["trigger_look"] = static (system, spawnInfo) => new TriggerLook(system, spawnInfo),
        ["trigger_teleport"] = static (system, spawnInfo) => new TriggerTeleport(system, spawnInfo),
        ["point_teleport"] = static (system, spawnInfo) => new PointTeleport(system, spawnInfo),
        ["point_entity_fader"] = static (system, spawnInfo) => new PointEntityFader(system, spawnInfo),

        // lights
        ["light_barn"] = static (system, spawnInfo) => new LightEntity(system, spawnInfo),
        ["light_environment"] = static (system, spawnInfo) => new LightEntity(system, spawnInfo),
        ["light_omni"] = static (system, spawnInfo) => new LightEntity(system, spawnInfo),
        ["light_omni2"] = static (system, spawnInfo) => new LightEntity(system, spawnInfo),
        ["light_ortho"] = static (system, spawnInfo) => new LightEntity(system, spawnInfo),
        ["light_rect"] = static (system, spawnInfo) => new LightEntity(system, spawnInfo),
        ["light_spot"] = static (system, spawnInfo) => new LightEntity(system, spawnInfo),

        // environment
        ["env_cubemap"] = static (system, spawnInfo) => new EnvCubemap(system, spawnInfo, isSphere: true),
        ["env_cubemap_box"] = static (system, spawnInfo) => new EnvCubemap(system, spawnInfo, isSphere: false),
        ["env_cubemap_fog"] = static (system, spawnInfo) => new EnvCubemapFog(system, spawnInfo),
        ["env_fog_controller"] = static (system, spawnInfo) => new EnvFogController(system, spawnInfo),
        ["env_combined_light_probe_volume"] = static (system, spawnInfo) => new EnvLightProbeVolume(system, spawnInfo, bakesCubemap: true),
        ["env_light_probe_volume"] = static (system, spawnInfo) => new EnvLightProbeVolume(system, spawnInfo, bakesCubemap: false),
        ["env_gradient_fog"] = static (system, spawnInfo) => new EnvGradientFog(system, spawnInfo),
        ["env_volumetric_fog_controller"] = static (system, spawnInfo) => new EnvVolumetricFogController(system, spawnInfo),
        ["env_volumetric_fog_volume"] = static (system, spawnInfo) => new EnvVolumetricFogVolume(system, spawnInfo),
        ["env_global_light"] = static (system, spawnInfo) => new EnvGlobalLight(system, spawnInfo),
        ["env_sky"] = static (system, spawnInfo) => new EnvSky(system, spawnInfo),
        ["ent_dota_lightinfo"] = static (system, spawnInfo) => new DotaLightInfo(system, spawnInfo),
        ["env_tonemap_controller"] = static (system, spawnInfo) => new EnvTonemapController(system, spawnInfo),
        ["info_map_parameters"] = static (system, spawnInfo) => new InfoMapParameters(system, spawnInfo),

        // particles
        ["env_explosion"] = static (system, spawnInfo) => new EnvExplosion(system, spawnInfo),
        ["info_particle_system"] = static (system, spawnInfo) => new InfoParticleSystem(system, spawnInfo),
        ["dota_world_particle_system"] = static (system, spawnInfo) => new InfoParticleSystem(system, spawnInfo),
        ["env_particle_glow"] = static (system, spawnInfo) => new EnvParticleGlow(system, spawnInfo),

        // models with an ambient effect
        ["dcg_game_board_attachment"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),
        ["ent_dota_fountain"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),
        ["ent_dota_tree"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),
        ["npc_dota_barracks"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),
        ["npc_dota_building"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),
        ["npc_dota_fort"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),
        ["npc_dota_lotus_pool"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),
        ["npc_dota_mango_tree"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),
        ["npc_dota_tower"] = static (system, spawnInfo) => new AmbientEffectEntity(system, spawnInfo),

        // A rope's effect_name is the cable effect. Drawn without the rope's snapshot, it is an
        // editor placeholder at the world origin. Ropes must never be treated as plain particle entities
        ["path_particle_rope"] = static (system, spawnInfo) => new PathParticleRopeEntity(system, spawnInfo),
        ["path_particle_rope_clientside"] = static (system, spawnInfo) => new PathParticleRopeEntity(system, spawnInfo),
        ["citadel_zipline_path"] = static (system, spawnInfo) => new PathParticleRopeEntity(system, spawnInfo),
        ["post_processing_volume"] = static (system, spawnInfo) => new PostProcessingVolume(system, spawnInfo),

        // cameras and spawn points
        ["point_camera"] = static (system, spawnInfo) => new PointCamera(system, spawnInfo),
        ["point_camera_vertical_fov"] = static (system, spawnInfo) => new PointCamera(system, spawnInfo),
        ["point_devshot_camera"] = static (system, spawnInfo) => new PointCamera(system, spawnInfo),
        ["sky_camera"] = static (system, spawnInfo) => new SkyCamera(system, spawnInfo),
        ["skybox_reference"] = static (system, spawnInfo) => new SkyboxReference(system, spawnInfo),
        ["sky_camera_volume"] = static (system, spawnInfo) => new SkyCameraVolume(system, spawnInfo),
        ["sky_camera_volume_target"] = static (system, spawnInfo) => new SkyCameraVolumeTarget(system, spawnInfo),
        ["info_player_counterterrorist"] = static (system, spawnInfo) => new SpawnPoint(system, spawnInfo),
        ["info_player_start"] = static (system, spawnInfo) => new SpawnPoint(system, spawnInfo),
        ["info_player_start_badguys"] = static (system, spawnInfo) => new SpawnPoint(system, spawnInfo),
        ["info_player_start_dota"] = static (system, spawnInfo) => new SpawnPoint(system, spawnInfo),
        ["info_player_start_goodguys"] = static (system, spawnInfo) => new SpawnPoint(system, spawnInfo),
        ["info_player_terrorist"] = static (system, spawnInfo) => new SpawnPoint(system, spawnInfo),
        ["info_team_spawn"] = static (system, spawnInfo) => new SpawnPoint(system, spawnInfo),
        ["team_select"] = static (system, spawnInfo) => new SpawnPoint(system, spawnInfo),

        // logic
        ["logic_auto"] = static (system, spawnInfo) => new LogicAuto(system, spawnInfo),
        ["logic_branch"] = static (system, spawnInfo) => new LogicBranch(system, spawnInfo),
        ["logic_branch_listener"] = static (system, spawnInfo) => new LogicBranchListener(system, spawnInfo),
        ["logic_case"] = static (system, spawnInfo) => new LogicCase(system, spawnInfo),
        ["logic_choreographed_scene"] = static (system, spawnInfo) => new LogicChoreographedScene(system, spawnInfo),
        ["logic_compare"] = static (system, spawnInfo) => new LogicCompare(system, spawnInfo),
        ["logic_relay"] = static (system, spawnInfo) => new LogicRelay(system, spawnInfo),
        ["logic_timer"] = static (system, spawnInfo) => new LogicTimer(system, spawnInfo),
        ["math_counter"] = static (system, spawnInfo) => new MathCounter(system, spawnInfo),
        ["filter_activator_model"] = static (system, spawnInfo) => new FilterActivatorModel(system, spawnInfo),
        ["filter_activator_team"] = static (system, spawnInfo) => new FilterActivatorTeam(system, spawnInfo),
        ["info_spawngroup_load_unload"] = static (system, spawnInfo) => new InfoSpawnGroupLoadUnload(system, spawnInfo),
        ["point_template"] = static (system, spawnInfo) => new PointTemplate(system, spawnInfo),

        // sounds
        ["env_soundscape"] = static (system, spawnInfo) => new EnvSoundscape(system, spawnInfo),
        ["point_soundevent"] = static (system, spawnInfo) => new PointSoundEvent(system, spawnInfo),
        ["snd_event_point"] = static (system, spawnInfo) => new PointSoundEvent(system, spawnInfo),
        ["snd_soundscape"] = static (system, spawnInfo) => new EnvSoundscape(system, spawnInfo),
        ["ambient_generic"] = static (system, spawnInfo) => new AmbientGeneric(system, spawnInfo),

        // panorama world panels
        ["point_clientui_world_panel"] = static (system, spawnInfo) => new PointClientUIWorldPanel(system, spawnInfo),
        ["point_clientui_world_text_panel"] = static (system, spawnInfo) => new PointClientUIWorldPanel(system, spawnInfo),
        ["point_clientui_world_movie_panel"] = static (system, spawnInfo) => new PointClientUIWorldPanel(system, spawnInfo),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the entity for a classname. The entity system adds it to the world and spawns it with the rest
    /// of the spawn group. An unimplemented classname gives a
    /// <see cref="GenericModelEntity"/> when it has a model, otherwise a <see cref="GenericEntity"/>.
    /// </summary>
    /// <returns>
    /// The entity, or <see langword="null"/> when the keyvalues name no classname or a <c>worldspawn</c>.
    /// </returns>
    public static BaseEntity? Create(EntitySystem system, EntitySpawnInfo spawnInfo)
    {
        var classname = spawnInfo.Data.GetStringProperty("classname");

        // worldspawn keyvalues are the map's world settings; the world entity belongs to the entity system
        if (classname == null || classname.Equals("worldspawn", StringComparison.OrdinalIgnoreCase))
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

        return entity;
    }
}
