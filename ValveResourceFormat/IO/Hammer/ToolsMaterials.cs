using ValveKeyValue;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;

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
}
