using System.IO;
using ValveKeyValue;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

/// <summary>
/// Hammer's tools materials, with the attributes that decide what their faces compile to, so their collision is
/// known without the game that has them and compiled collision can be traced back to the material it came from.
/// </summary>
public static class ToolsMaterials
{
    /// <summary>The material of faces that are not drawn, and of collision no other tools material is found for.</summary>
    public const string Nodraw = "materials/tools/toolsnodraw.vmat";

    /// <summary>The material of occluders.</summary>
    public const string Occluder = "materials/tools/toolsoccluder.vmat";

    /// <summary>The material volumes of entities are built from, unless their class applies another.</summary>
    public const string Trigger = "materials/tools/toolstrigger.vmat";

    private readonly record struct ToolsLabel(string Name, Color32 Background);

    // Most classes keep the orange of the trigger material, and only a few that matter when looking around a map stand out
    private static readonly Color32 DefaultTriggerBackground = new(255, 156, 0);

    // Layers that no tools material compiles to are rare, so they share one color apart from every tools material
    private static readonly Color32 CollisionBackground = new(140, 150, 170);

    // Classes that do the same kind of thing share a color, such as red for damage, purple for teleports and blue for movement
    private static readonly Dictionary<string, Color32> TriggerBackgrounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ambient_creatures_zone"] = new(110, 190, 60),
        ["citadel_capture_point"] = new(255, 90, 130),
        ["citadel_invis_volume"] = new(150, 150, 170),
        ["citadel_obscured_volume"] = new(150, 150, 170),
        ["citadel_passthrough_fake_wall"] = new(255, 120, 200),
        ["citadel_trigger_climb_rope"] = new(240, 220, 60),
        ["citadel_trigger_corrupted_item_shop"] = new(150, 230, 70),
        ["citadel_trigger_hideout_teleport"] = new(190, 120, 255),
        ["citadel_trigger_hotel_exit"] = new(120, 170, 200),
        ["citadel_trigger_idol_return"] = new(255, 90, 130),
        ["citadel_trigger_in_map_district"] = new(120, 170, 200),
        ["citadel_trigger_interior"] = new(120, 170, 200),
        ["citadel_trigger_no_portals"] = new(190, 120, 255),
        ["citadel_trigger_shop_tunnel"] = new(150, 230, 70),
        ["citadel_trigger_teleport"] = new(190, 120, 255),
        ["citadel_tunnel_trigger"] = new(70, 200, 255),
        ["cs_minimap_volume"] = new(120, 170, 200),
        ["func_bomb_target"] = new(255, 90, 130),
        ["func_buyzone"] = new(150, 230, 70),
        ["func_clip_vphysics"] = new(211, 58, 33),
        ["func_conveyor"] = new(70, 200, 255),
        ["func_hostage_rescue"] = new(90, 230, 220),
        ["func_regenerate"] = new(100, 220, 110),
        ["func_water"] = new(40, 120, 255),
        ["trigger_autosave"] = new(100, 220, 110),
        ["trigger_blind_zombie_sound_area"] = new(255, 230, 140),
        ["trigger_blind_zombie_wander_area"] = new(255, 230, 140),
        ["trigger_bomb_reset"] = new(255, 90, 130),
        ["trigger_boss_attackable"] = new(255, 90, 130),
        ["trigger_catapult"] = new(70, 200, 255),
        ["trigger_changelevel"] = new(100, 220, 110),
        ["trigger_detect_bullet_fire"] = new(255, 230, 140),
        ["trigger_detect_explosion"] = new(255, 230, 140),
        ["trigger_foliage_interaction"] = new(110, 190, 60),
        ["trigger_gravity"] = new(70, 200, 255),
        ["trigger_hero"] = new(255, 230, 140),
        ["trigger_hostage_reset"] = new(90, 230, 220),
        ["trigger_hurt"] = new(255, 80, 70),
        ["trigger_hurt_citadel"] = new(255, 80, 70),
        ["trigger_item_shop"] = new(150, 230, 70),
        ["trigger_item_shop_safe_zone"] = new(150, 230, 70),
        ["trigger_look"] = new(255, 230, 140),
        ["trigger_midboss_shield"] = new(90, 230, 220),
        ["trigger_push"] = new(70, 200, 255),
        ["trigger_shop"] = new(150, 230, 70),
        ["trigger_soundscape"] = new(0, 232, 203),
        ["trigger_team_base"] = new(120, 170, 200),
        ["trigger_teleport"] = new(190, 120, 255),
        ["trigger_tier3phase2_shield"] = new(90, 230, 220),
        ["trigger_transition"] = new(100, 220, 110),
        ["trigger_traversal_invalid_spot"] = new(240, 220, 60),
        ["trigger_traversal_modifier"] = new(240, 220, 60),
        ["trigger_traversal_no_teleport"] = new(240, 220, 60),
        ["trigger_traversal_tp_interrupt"] = new(240, 220, 60),
        ["trigger_xen_foliage_interaction"] = new(110, 190, 60),
    };

    // Game and brush entity prefixes say nothing about the volume, and are stripped in order so that
    // "citadel_trigger_teleport" reads "trigger_teleport"
    private static readonly string[] TriggerNamePrefixes = ["citadel_", "dota_", "cs_", "hl_vr_", "func_"];

    // The name written on the generated texture of each tools material, with underscores between its words, and the
    // most common color of its texture, which the generated texture is filled with. Where the texture writes dark text
    // on a dark color or light text on a light one, the color is made lighter or darker so that the text follows it.
    private static readonly Dictionary<string, ToolsLabel> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["materials/tools_postprocess_volume.vmat"] = new("post_process_volume", new(0, 254, 186)),
        ["materials/tools/audioblocker.vmat"] = new("audio_blocker", new(0, 232, 203)),
        ["materials/tools/audioblocker_inner.vmat"] = new("audio_blocker_inner", new(0, 232, 203)),
        ["materials/tools/audioblocker_mid.vmat"] = new("audio_blocker_mid", new(0, 232, 203)),
        ["materials/tools/audioblocker_outer.vmat"] = new("audio_blocker_outer", new(0, 232, 203)),
        ["materials/tools/audiomodifier.vmat"] = new("audio_modifier", new(37, 189, 252)),
        ["materials/tools/cameraclip.vmat"] = new("camera_clip", new(40, 152, 153)),
        ["materials/tools/climb.vmat"] = new("climb", new(0, 0, 0)),
        ["materials/tools/climb_versus.vmat"] = new("climb_versus", new(0, 0, 0)),
        ["materials/tools/dota_nav_walkable.vmat"] = new("nav_walkable", new(25, 120, 0)),
        ["materials/tools/fogvolume.vmat"] = new("fog_volume", new(197, 0, 255)),
        ["materials/tools/mapedgeclip.vmat"] = new("map_edge_clip", new(145, 146, 145)),
        ["materials/tools/nonavclip.vmat"] = new("no_nav_clip", new(211, 59, 33)),
        ["materials/tools/skyvisblocker.vmat"] = new("sky_vis_blocker", new(93, 195, 255)),
        ["materials/tools/tools_conveyor.vmat"] = new("conveyor", new(0, 254, 58)),
        ["materials/tools/tools_cs_place.vmat"] = new("place", new(162, 255, 0)),
        ["materials/tools/tools_func_occluder.vmat"] = new("func_occluder", new(161, 178, 255)),
        ["materials/tools/tools_no_wards.vmat"] = new("no_wards", new(255, 254, 255)),
        ["materials/tools/tools_spawn_volume.vmat"] = new("spawn_volume", new(255, 128, 0)),
        ["materials/tools/toolsblock_los.vmat"] = new("block_los", new(0, 0, 255)),
        ["materials/tools/toolsblockbomb.vmat"] = new("block_bomb", new(50, 212, 177)),
        ["materials/tools/toolsblockbullets.vmat"] = new("block_bullets", new(255, 130, 5)),
        ["materials/tools/toolsblockbullets_cs.vmat"] = new("block_bullets", new(255, 129, 5)),
        ["materials/tools/toolsblocklight.vmat"] = new("block_light", new(110, 150, 12)),
        ["materials/tools/toolsblocksound.vmat"] = new("block_sound", new(116, 148, 2)),
        ["materials/tools/toolsclip.vmat"] = new("clip", new(211, 58, 33)),
        ["materials/tools/toolscontrolclip.vmat"] = new("control_clip", new(210, 32, 173)),
        ["materials/tools/toolscreatureclip.vmat"] = new("creature_clip", new(219, 93, 8)),
        ["materials/tools/toolsfog.vmat"] = new("fog", new(150, 146, 145)),
        ["materials/tools/toolsforceunseenclip.vmat"] = new("force_unseen_clip", new(140, 33, 211)),
        ["materials/tools/toolsgrenadeclip.vmat"] = new("grenade_clip", new(185, 255, 18)),
        ["materials/tools/toolsheroclip.vmat"] = new("hero_clip", new(140, 33, 211)),
        ["materials/tools/toolsignorecharacter.vmat"] = new("ignore_character", new(88, 23, 200)),
        ["materials/tools/toolsignorenpc.vmat"] = new("ignore_npc", new(186, 109, 156)),
        ["materials/tools/toolsinvisible.vmat"] = new("invisible", new(254, 141, 193)),
        ["materials/tools/toolsinvisibleladder.vmat"] = new("ladder", new(254, 152, 123)),
        ["materials/tools/toolsinvisibleladder_wood.vmat"] = new("ladder_wood", new(254, 152, 123)),
        ["materials/tools/toolslightmapres.vmat"] = new("lightmap_res", new(164, 129, 4)),
        ["materials/tools/toolslightprobevolume.vmat"] = new("light_probe_volume", new(255, 200, 16)),
        ["materials/tools/toolslightvolumeboundary.vmat"] = new("light_volume_boundary", new(115, 191, 255)),
        ["materials/tools/toolsmarkupvolumeai.vmat"] = new("markup_volume_ai", new(63, 204, 222)),
        ["materials/tools/toolsnavattribute.vmat"] = new("nav_attribute", new(171, 174, 255)),
        ["materials/tools/toolsnavclip.vmat"] = new("nav_clip", new(197, 8, 166)),
        ["materials/tools/toolsnavspaceclip.vmat"] = new("nav_space_clip", new(154, 186, 228)),
        ["materials/tools/toolsnodraw.vmat"] = new("nodraw", new(252, 217, 35)),
        ["materials/tools/toolsnodraw_bounce.vmat"] = new("nodraw_bounce", new(252, 217, 35)),
        ["materials/tools/toolsnodraw_grate.vmat"] = new("nodraw_grate", new(178, 251, 35)),
        ["materials/tools/toolsnodraw_metal.vmat"] = new("nodraw_metal", new(252, 217, 35)),
        ["materials/tools/toolsnodraw_plastic.vmat"] = new("nodraw_plastic", new(252, 217, 35)),
        ["materials/tools/toolsnodraw_wood.vmat"] = new("nodraw_wood", new(252, 217, 35)),
        ["materials/tools/toolsnpcclip.vmat"] = new("npc_clip", new(140, 32, 210)),
        ["materials/tools/toolsoccluder.vmat"] = new("occluder", new(112, 228, 37)),
        ["materials/tools/toolsotherclip.vmat"] = new("other_clip", new(121, 32, 208)),
        ["materials/tools/toolsplayerclip.vmat"] = new("player_clip", new(197, 0, 123)),
        ["materials/tools/toolsplayerskyclip.vmat"] = new("player_sky_clip", new(197, 0, 122)),
        ["materials/tools/toolsprecipitation.vmat"] = new("precipitation", new(141, 191, 224)),
        ["materials/tools/toolsrenderportal.vmat"] = new("render_portal", new(0, 254, 254)),
        ["materials/tools/toolsskip.vmat"] = new("skip", new(37, 189, 252)),
        ["materials/tools/toolsskybox.vmat"] = new("skybox", new(178, 224, 252)),
        ["materials/tools/toolsskybox_occluder.vmat"] = new("skybox_occluder", new(203, 177, 252)),
        ["materials/tools/toolsslide.vmat"] = new("slide", new(210, 142, 32)),
        ["materials/tools/toolssolidblocklight.vmat"] = new("solid_block_light", new(152, 194, 74)),
        ["materials/tools/toolssteamaudiofullabsorption.vmat"] = new("steam_audio_full_absorption", new(255, 154, 104)),
        ["materials/tools/toolssteamaudiofulldiffuse.vmat"] = new("steam_audio_full_diffuse", new(255, 154, 104)),
        ["materials/tools/toolsteleportclip.vmat"] = new("teleport_clip", new(1, 152, 164)),
        ["materials/tools/toolstrigger.vmat"] = new("trigger", DefaultTriggerBackground),
        ["materials/tools/toolswardonly.vmat"] = new("ward_only", new(38, 189, 252)),
        ["materials/tools/toolswardsonly.vmat"] = new("wards_only", new(37, 251, 129)),
        ["materials/tools/visblocker.vmat"] = new("vis_blocker", new(93, 195, 255)),
    };

    private sealed record ToolsMaterial(string Path, string[] Attributes, string? CollisionProperty = null, string? SurfaceProperty = null);

    private static readonly ToolsMaterial[] Materials =
    [
        new("materials/tools/toolsclip.vmat", ["mapbuilder.nodraw", "mapbuilder.npcclip", "mapbuilder.playerclip"]),
        new("materials/tools/toolsplayerclip.vmat", ["mapbuilder.nodraw", "mapbuilder.playerclip"]),
        new("materials/tools/toolsnpcclip.vmat", ["mapbuilder.nodraw", "mapbuilder.npcclip"]),
        new("materials/tools/toolsinvisible.vmat", ["mapbuilder.nodraw", "mapbuilder.npcclip", "mapbuilder.passbullets"]),
        new("materials/tools/toolsinvisibleladder.vmat", ["mapbuilder.nodraw", "mapbuilder.ladder", "mapbuilder.npcclip", "mapbuilder.playerclip"], SurfaceProperty: "ladder"),
        new("materials/tools/toolsinvisibleladder_wood.vmat", ["mapbuilder.nodraw", "mapbuilder.ladder", "mapbuilder.passbullets"], SurfaceProperty: "Wood_Ladder"),
        new("materials/tools/climb.vmat", ["mapbuilder.nodraw", "mapbuilder.ladder", "mapbuilder.infectedladder", "mapbuilder.passbullets"]),
        new("materials/tools/climb_versus.vmat", ["mapbuilder.ladder", "mapbuilder.infectedladder", "mapbuilder.passbullets"]),
        new("materials/tools/toolsnodraw_grate.vmat", ["mapbuilder.nodraw", "mapbuilder.passbullets"]),
        new("materials/tools/toolsblock_los.vmat", ["mapbuilder.nodraw", "mapbuilder.blocklos"]),
        new("materials/tools/toolsblocksound.vmat", ["mapbuilder.nodraw", "mapbuilder.blocksound"]),
        new("materials/tools/toolsblocksound.vmat", ["mapbuilder.nodraw"], CollisionProperty: "default_block_sound"),
        new("materials/tools/audioblocker.vmat", ["mapbuilder.nodraw"], CollisionProperty: "no_collision_block_sound", SurfaceProperty: "audioblocker"),
        new("materials/tools/audioblocker_inner.vmat", ["mapbuilder.nodraw"], CollisionProperty: "no_collision_block_sound", SurfaceProperty: "audioblocker_inneredge"),
        new("materials/tools/audioblocker_mid.vmat", ["mapbuilder.nodraw"], CollisionProperty: "no_collision_block_sound", SurfaceProperty: "audioblocker_middleedge"),
        new("materials/tools/audioblocker_outer.vmat", ["mapbuilder.nodraw"], CollisionProperty: "no_collision_block_sound", SurfaceProperty: "audioblocker_outeredge"),
        new("materials/tools/toolsskybox.vmat", ["mapbuilder.nodraw", "mapbuilder.sky"]),
        new("materials/tools/skyvisblocker.vmat", ["mapbuilder.nodraw", "mapbuilder.sky"]),
        new("materials/tools/toolsteleportclip.vmat", ["mapbuilder.nodraw", "mapbuilder.teleportclip"]),
        new("materials/tools/toolsnavclip.vmat", ["mapbuilder.nodraw", "mapbuilder.navclip"]),
        new("materials/tools/tools_navarea.vmat", ["mapbuilder.nodraw", "mapbuilder.npcclip", "mapbuilder.playerclip"]),
        new("materials/tools/toolsgrenadeclip.vmat", ["mapbuilder.nodraw"], CollisionProperty: "csgo_grenadeclip"),
        new("materials/tools/toolsplayerskyclip.vmat", ["mapbuilder.nodraw", "mapbuilder.playerclip"], CollisionProperty: "Citadel_Skyclip"),
        new("materials/tools/toolsslide.vmat", ["mapbuilder.nodraw", "mapbuilder.playerclip"], SurfaceProperty: "ice"),
        new("materials/tools/toolsignorenpc.vmat", ["mapbuilder.nodraw"], CollisionProperty: "default_no_npc"),
        new("materials/tools/toolsignorecharacter.vmat", ["mapbuilder.nodraw"], CollisionProperty: "default_no_npc_no_player_movement"),
        new("materials/tools/toolsnodraw.vmat", ["mapbuilder.nodraw"]),
        new("materials/tools/toolsnodraw_bounce.vmat", ["mapbuilder.nodraw"], SurfaceProperty: "floor_bounce"),
        new("materials/tools/toolsnodraw_metal.vmat", ["mapbuilder.nodraw"], SurfaceProperty: "metal"),
        new("materials/tools/toolsnodraw_plastic.vmat", ["mapbuilder.nodraw"], SurfaceProperty: "prop.plastic"),
        new("materials/tools/toolsnodraw_wood.vmat", ["mapbuilder.nodraw"], SurfaceProperty: "world.wood_solid"),
        new("materials/tools/toolsblockbullets.vmat", ["mapbuilder.nodraw"]),
        new("materials/tools/toolsblockbullets_cs.vmat", ["mapbuilder.nodraw"], SurfaceProperty: "blockbullets"),
        new("materials/tools/toolsblockbomb.vmat", ["mapbuilder.nodraw"]),
        new("materials/tools/toolscontrolclip.vmat", ["mapbuilder.nodraw"]),
        new("materials/tools/toolsotherclip.vmat", ["mapbuilder.nodraw", "mapbuilder.otherclip"]),
        new("materials/tools/toolsnavspaceclip.vmat", ["mapbuilder.nodraw"]),
        new("materials/tools/toolsfog.vmat", ["mapbuilder.nodraw"]),
        new("materials/tools/toolsmarkupvolumeai.vmat", ["mapbuilder.nodraw"]),
        new("materials/tools/toolslightvolumeboundary.vmat", ["mapbuilder.nodraw"]),
        new("materials/tools/toolstrigger.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/toolsskip.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/toolsnavattribute.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/toolsprecipitation.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/toolslightmapres.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/toolslightprobevolume.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/toolssteamaudiofullabsorption.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"], SurfaceProperty: "default_silent"),
        new("materials/tools/toolssteamaudiofulldiffuse.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/tools_conveyor.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"], SurfaceProperty: "rubber"),
        new("materials/tools/tools_cs_place.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/tools_imageplane.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/tools_spawn_volume.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/audiomodifier.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/fogvolume.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/visblocker.vmat", ["mapbuilder.nodraw", "mapbuilder.nonsolid"]),
        new("materials/tools/toolsblocklight.vmat", ["mapbuilder.nonsolid"]),
        new("materials/tools/toolsskybox_occluder.vmat", ["mapbuilder.nonsolid"]),
        new("materials/tools/toolsoccluder.vmat", ["mapbuilder.nonsolid", "nocollision"]),
        new("materials/tools/tools_func_occluder.vmat", ["mapbuilder.nonsolid", "nocollision"]),
    ];

    private static readonly (string Path, MaterialCollision Collision)[] Collisions = CreateCollisions();

    private static (string Path, MaterialCollision Collision)[] CreateCollisions()
    {
        var collisions = new (string Path, MaterialCollision Collision)[Materials.Length];

        for (var i = 0; i < Materials.Length; i++)
        {
            var material = Materials[i];
            var intAttributes = new Dictionary<string, long>();
            var stringAttributes = new Dictionary<string, string>();

            foreach (var attribute in material.Attributes)
            {
                intAttributes[attribute] = 1;
            }

            if (material.CollisionProperty != null)
            {
                stringAttributes["mapbuilder.collisionproperties"] = material.CollisionProperty;
            }

            if (material.SurfaceProperty != null)
            {
                stringAttributes["PhysicsSurfaceProperties"] = material.SurfaceProperty;
            }

            collisions[i] = (material.Path, new MaterialCollision(intAttributes, stringAttributes));
        }

        return collisions;
    }

    /// <summary>
    /// Gets the collision faces with a tools material compile to.
    /// </summary>
    /// <param name="material">The path of the material.</param>
    /// <returns>The collision, or <see langword="null"/> when it is not a tools material whose collision differs from that of other materials.</returns>
    public static MaterialCollision? GetCollision(string material)
    {
        foreach (var (path, collision) in Collisions)
        {
            if (path.Equals(material, StringComparison.OrdinalIgnoreCase))
            {
                return collision;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the tools material whose faces compile to the given collision. Collision in the default group with no
    /// layers of its own is not traced back, as faces with any other material compile to it too.
    /// </summary>
    /// <param name="collisionAttributes">Collision attributes of compiled physics, see <see cref="PhysAggregateData.CollisionAttributes"/>.</param>
    /// <param name="surfaceProperty">The surface property of the collision, which picks between materials that compile to the same attributes.</param>
    /// <returns>The path of the material, or <see langword="null"/> when no tools material compiles to that collision.</returns>
    public static string? GetForCollision(KVObject collisionAttributes, string? surfaceProperty = null)
    {
        ArgumentNullException.ThrowIfNull(collisionAttributes);

        string? found = null;

        foreach (var (path, collision) in Collisions)
        {
            if (collision.CollisionGroup.Length == 0 || !collision.CompilesTo(collisionAttributes))
            {
                continue;
            }

            if (surfaceProperty != null && collision.SurfaceProperty.Equals(surfaceProperty, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            found ??= path;
        }

        return found;
    }

    /// <summary>
    /// Gets the tools material whose faces compile to the collision of a model, see <see cref="GetForCollision"/>.
    /// The collision of a prop compiles to the same attributes as a tools material of the default group that excludes
    /// a layer, so a model that draws meshes only traces back collision in a layer of its own.
    /// </summary>
    /// <param name="collisionAttributes">Collision attributes of compiled physics, see <see cref="PhysAggregateData.CollisionAttributes"/>.</param>
    /// <param name="drawsMeshes">Whether the model draws meshes, rather than being only collision.</param>
    /// <returns>The path of the material, or <see langword="null"/> when no tools material compiles to that collision.</returns>
    public static string? GetForModelCollision(KVObject collisionAttributes, bool drawsMeshes)
    {
        if (drawsMeshes && PhysAggregateData.GetInteractAsTags(collisionAttributes).Length == 0)
        {
            return null;
        }

        return GetForCollision(collisionAttributes);
    }

    /// <summary>
    /// Gets the tools material the volume of an entity is built from: the one its class applies, or
    /// <see cref="Trigger"/> where it applies none.
    /// </summary>
    /// <param name="entityClassName">The class of the entity.</param>
    /// <param name="fileLoader">The file loader of the map, which decides the game whose classes are used.</param>
    /// <returns>The path of the material, or <see langword="null"/> when no class is given.</returns>
    public static string? GetForEntity(string? entityClassName, IFileLoader fileLoader)
    {
        if (string.IsNullOrEmpty(entityClassName))
        {
            return null;
        }

        return EntityClassCatalog.GetTable(fileLoader).FindClass(entityClassName)?.AutoApplyMaterial ?? Trigger;
    }

    /// <summary>
    /// Describes a generated texture that names what faces were built from: a tools material, the class of an entity volume, or
    /// the layers of collision.
    /// </summary>
    /// <param name="Key">A name for the texture, unique to what it names, which it is cached and exported under.</param>
    /// <param name="Name">The name written on the texture, with underscores between its words.</param>
    /// <param name="Background">The color the texture is filled with.</param>
    public readonly record struct TextureLabel(string Key, string Name, Color32 Background)
    {
        /// <summary>
        /// Gets the color of the name: black on light backgrounds, and white on dark ones.
        /// </summary>
        public Color32 Text => GetTextColor(Background);
    }

    /// <summary>
    /// Gets the generated texture that names a tools material, filled with the color of its texture.
    /// </summary>
    /// <param name="material">The path of the material.</param>
    /// <returns>The label, or <see langword="null"/> when the material is not known.</returns>
    public static TextureLabel? GetToolsLabel(string material)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (!Labels.TryGetValue(material, out var label))
        {
            return null;
        }

        return new TextureLabel($"tool_{Path.GetFileNameWithoutExtension(material)}", label.Name, label.Background);
    }

    /// <summary>
    /// Gets the generated texture the volumes of entities of a class built from <see cref="Trigger"/> are shown with,
    /// so that volumes of different classes can be told apart.
    /// </summary>
    /// <param name="entityClassName">The class of the entity.</param>
    /// <returns>The label, the same for a class wherever it is shown.</returns>
    public static TextureLabel GetTriggerLabel(string entityClassName)
    {
        ArgumentNullException.ThrowIfNull(entityClassName);

        if (!TriggerBackgrounds.TryGetValue(entityClassName, out var background))
        {
            background = DefaultTriggerBackground;
        }

        return new TextureLabel($"trigger_{entityClassName}", StripTriggerNamePrefixes(entityClassName), background);
    }

    private static string StripTriggerNamePrefixes(string entityClassName)
    {
        var name = entityClassName;

        foreach (var prefix in TriggerNamePrefixes)
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
            }
        }

        return name;
    }

    // Oklab lightness is perceived the same for every hue, so one threshold suits every background
    private static Color32 GetTextColor(Color32 background)
    {
        var lightness = ColorSpace.LinearToOklab(background.ToLinearColor().AsVector3()).X;

        return lightness > 0.67f ? new Color32(0, 0, 0) : new Color32(255, 255, 255);
    }

    /// <summary>
    /// Gets the generated texture of collision that no tools material compiles to, see <see cref="GetForCollision"/>,
    /// which names the layers it interacts as.
    /// </summary>
    /// <param name="collisionAttributes">Collision attributes of compiled physics, see <see cref="PhysAggregateData.CollisionAttributes"/>.</param>
    /// <returns>The label, or <see langword="null"/> when the collision interacts as no layer.</returns>
    public static TextureLabel? GetCollisionLabel(KVObject collisionAttributes)
    {
        var tags = PhysAggregateData.GetInteractAsTags(collisionAttributes);

        if (tags.Length == 0)
        {
            return null;
        }

        var name = string.Join('_', tags).ToLowerInvariant();

        return new TextureLabel($"collision_{name}", name, CollisionBackground);
    }
}
