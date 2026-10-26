using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// The named collision properties games declare in <c>scripts/collision_properties.txt</c>, which materials and
/// model physics shapes refer to by name.
/// </summary>
public static class CollisionProperties
{
    /// <summary>A named collision property.</summary>
    /// <param name="Name">The name it is referred to by.</param>
    /// <param name="CollisionGroup">The collision group it puts collision in.</param>
    /// <param name="InteractAs">The interaction layers collision with it is in.</param>
    /// <param name="InteractWith">The interaction layers collision with it collides with.</param>
    /// <param name="InteractExclude">The interaction layers collision with it never collides with.</param>
    public sealed record Entry(string Name, string CollisionGroup, string[] InteractAs, string[] InteractWith, string[] InteractExclude);

    private const string ConditionallySolid = "ConditionallySolid";

    private static readonly Entry[] Entries =
    [
        new("default", "default", [], [], []),
        new("default_block_sound", ConditionallySolid, ["blocksound", "CONTENTS_SOLID"], [], []),
        new("no_collision_block_sound", ConditionallySolid, ["blocksound"], [], []),
        new("solid_no_block_los", ConditionallySolid, ["CONTENTS_SOLID_NO_BLOCK_LOS"], [], []),
        new("window", ConditionallySolid, ["window"], [], []),
        new("grating", ConditionallySolid, ["passbullets"], [], []),
        new("csgo_grenadeclip", ConditionallySolid, ["csgo_grenadeclip"], [], []),
        new("csgo_droneclip", ConditionallySolid, ["csgo_droneclip"], [], []),
        new("default_player_movement_exclude", "default", [], [], ["player"]),
        new("clip", ConditionallySolid, ["playerclip", "npcclip"], [], []),
        new("csgo_railing", ConditionallySolid, ["passbullets"], [], ["csgo_thrown_grenade"]),
        new("csgo_railing_no_players", ConditionallySolid, ["passbullets"], [], ["csgo_thrown_grenade", "player"]),
        new("foliage_no_collision_pass_bullets", ConditionallySolid, [], [], ["player", "npc"]),
        new("default_grenade_throw_exclude", "default", [], [], ["csgo_thrown_grenade"]),
        new("default_player_movement_and_thrown_grenade_exclude", "default", [], [], ["player", "csgo_thrown_grenade"]),
        new("vr_hand", ConditionallySolid, [], [], []),
        new("npc_clip", ConditionallySolid, ["npcclip"], [], []),
        new("default_no_npc", "default", [], [], ["npc"]),
        new("default_no_npc_no_player_continuous", "default", [], [], ["npc", "vr_player_continuous"]),
        new("default_no_npc_no_player_movement", "default", [], [], ["npc", "vr_player_movement"]),
        new("breakable_window", "breakable_glass", ["window"], [], []),
        new("teleportclip", ConditionallySolid, ["teleportclip"], [], []),
        new("los_blocker", ConditionallySolid, ["blocklos"], [], []),
        new("citadel_grating", ConditionallySolid, ["solid", "Citadel_BlockCamera", "Citadel_Camera_Can_Pass_Through"], [], ["Citadel_Ability", "Citadel_Bullet"]),
        new("citadel_foliage", ConditionallySolid, ["Citadel_Foliage"], [], ["Citadel_Projectile"]),
        new("citadel_skyclip", ConditionallySolid, ["playerclip", "Citadel_Skyclip"], [], []),
        new("block_mantle", ConditionallySolid, ["Citadel_BlockMantle"], [], []),
        new("camera_can_pass_through", ConditionallySolid, ["solid", "Citadel_Camera_Can_Pass_Through"], [], []),
        new("ping_target", ConditionallySolid, ["Citadel_Valid_Ping_Target"], [], []),
        new("citadel_npc", "NPC", ["npc"], [], []),
    ];

    /// <summary>
    /// Finds a collision property by its name, ignoring case.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The collision property, or <see langword="null"/> when none has that name.</returns>
    public static Entry? Find(string name)
    {
        foreach (var entry in Entries)
        {
            if (entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first collision property whose collision group and interaction layers compile to the given
    /// collision attributes.
    /// </summary>
    /// <param name="collisionAttributes">Collision attributes of compiled physics, see <see cref="PhysAggregateData.CollisionAttributes"/>.</param>
    /// <returns>The collision property, or <see langword="null"/> when none compiles to them.</returns>
    public static Entry? FindCompiledFrom(KVObject collisionAttributes)
    {
        ArgumentNullException.ThrowIfNull(collisionAttributes);

        foreach (var entry in Entries)
        {
            if (CompilesTo(collisionAttributes, entry.CollisionGroup, entry.InteractAs, entry.InteractWith, entry.InteractExclude))
            {
                return entry;
            }
        }

        return null;
    }

    // Whether collision in a group with these layers compiles to the given collision attributes
    internal static bool CompilesTo(KVObject collisionAttributes, string collisionGroup, IReadOnlyList<string> interactAs,
        IReadOnlyList<string> interactWith, IReadOnlyList<string> interactExclude)
        => collisionGroup.Equals(collisionAttributes.GetStringProperty("m_CollisionGroupString"), StringComparison.OrdinalIgnoreCase)
            && HaveSameLayers(interactAs, PhysAggregateData.GetInteractAsTags(collisionAttributes))
            && HaveSameLayers(interactWith, collisionAttributes.GetArray<string>("m_InteractWithStrings") ?? [])
            && HaveSameLayers(interactExclude, collisionAttributes.GetArray<string>("m_InteractExcludeStrings") ?? []);

    private static bool HaveSameLayers(IReadOnlyList<string> declared, string[] compiled)
    {
        var layers = new HashSet<string>(declared, StringComparer.OrdinalIgnoreCase);
        var compiledLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var layer in compiled)
        {
            if (layer.Length > 0)
            {
                compiledLayers.Add(layer);
            }
        }

        return layers.SetEquals(compiledLayers);
    }
}
