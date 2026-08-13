using System.Linq;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Entity classname to header hue for the entity I/O graph. Lookup order: exact overrides
/// (classes whose Source 2 Hammer editor icon dictates a color), the reserved sound hue,
/// family prefixes, Neutral. Pink is reserved for sound entities and Emerald for
/// point_template.
/// </summary>
internal static class EntityClassHues
{
    /// <summary>Header hue for an entity classname.</summary>
    /// <param name="classname">The entity classname to colour.</param>
    public static GraphHue For(string classname)
    {
        if (Overrides.TryGetValue(classname, out var hue))
        {
            return hue;
        }

        if (classname.Contains("sound", StringComparison.OrdinalIgnoreCase))
        {
            return GraphHue.Pink;
        }

        foreach (var (prefix, familyHue) in FamilyPrefixes)
        {
            if (classname.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return familyHue;
            }
        }

        return GraphHue.Neutral;
    }

    /// <summary>
    /// One row per hue the given classnames resolve to, labelled with the classnames that produced
    /// it. A family prefix collapses to "prefix*" only when every classname present under it lands
    /// on that one hue.
    /// </summary>
    /// <param name="classnames">The entity classnames drawn in the graph.</param>
    public static IEnumerable<GraphLegendEntry> Legend(IEnumerable<string> classnames)
    {
        ArgumentNullException.ThrowIfNull(classnames);

        var distinct = new SortedSet<string>(classnames, StringComparer.OrdinalIgnoreCase);

        // The reserved point_template hue leads; the rest follow by how much of the graph they colour.
        foreach (var group in distinct.GroupBy(For)
            .OrderByDescending(g => g.Key == GraphHue.Emerald)
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key))
        {
            yield return new(DescribeMembers([.. group], distinct), group.Key);
        }
    }

    /// <summary>
    /// Names a hue's members: families every present member of which shares the hue collapse to
    /// "prefix*", anything left is named outright, and a long tail becomes a count.
    /// </summary>
    private static string DescribeMembers(List<string> members, SortedSet<string> allPresent)
    {
        const int MaxTokens = 4;

        var remaining = new List<string>(members);
        var tokens = new List<string>();

        foreach (var (prefix, _) in FamilyPrefixes)
        {
            var mine = remaining.FindAll(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            // The family speaks for this hue only when every present member of it lands here.
            if (mine.Count < 2 || allPresent.Count(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) != mine.Count)
            {
                continue;
            }

            tokens.Add(prefix + "*");
            remaining.RemoveAll(c => mine.Contains(c));
        }

        tokens.AddRange(remaining);

        if (tokens.Count <= MaxTokens)
        {
            return string.Join(", ", tokens);
        }

        return string.Join(", ", tokens.Take(MaxTokens).Append($"+{tokens.Count - MaxTokens} more"));
    }

    private static readonly (string Prefix, GraphHue Hue)[] FamilyPrefixes =
    [
        ("ai_", GraphHue.Maroon),
        ("ambient_", GraphHue.Pink),
        ("env_", GraphHue.Teal),
        ("filter_", GraphHue.Olive),
        ("func_", GraphHue.Blue),
        ("game_", GraphHue.Maroon),
        ("item_", GraphHue.Amber),
        ("keyframe_", GraphHue.Indigo),
        ("light", GraphHue.Amber),
        ("logic_", GraphHue.Amber),
        ("math_", GraphHue.Olive),
        ("move_", GraphHue.Indigo),
        ("npc_", GraphHue.Maroon),
        ("path_", GraphHue.Indigo),
        ("phys", GraphHue.Amber),
        ("player_", GraphHue.Maroon),
        ("point_", GraphHue.Purple),
        ("prop_", GraphHue.Slate),
        ("snd_", GraphHue.Pink),
        ("trigger_", GraphHue.Orange),
        ("weapon_", GraphHue.Amber),
    ];

    private static readonly Dictionary<string, GraphHue> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ai_attached_item_manager"] = GraphHue.Green,
        ["ai_goal_lead_weapon"] = GraphHue.Olive,
        ["commentary_auto"] = GraphHue.Orange,
        ["env_explosion"] = GraphHue.Orange,
        ["env_fade"] = GraphHue.Orange,
        ["env_fire"] = GraphHue.Orange,
        ["env_firesource"] = GraphHue.Orange,
        ["env_physexplosion"] = GraphHue.Orange,
        ["env_physimpact"] = GraphHue.Orange,
        ["env_sky"] = GraphHue.Cyan,
        ["env_spark"] = GraphHue.Amber,
        ["env_volumetric_fog_volume"] = GraphHue.Cyan,
        ["env_wind"] = GraphHue.Blue,
        ["fog_volume"] = GraphHue.Cyan,
        ["game_end"] = GraphHue.Orange,
        ["game_text"] = GraphHue.Amber,
        ["gibshooter"] = GraphHue.Orange,
        ["haptic_relay"] = GraphHue.Orange,
        ["info_lighting"] = GraphHue.Green,
        ["info_particle_system"] = GraphHue.Orange,
        ["info_radar_target"] = GraphHue.Green,
        ["info_snipertarget"] = GraphHue.Green,
        ["info_spawngroup_landmark"] = GraphHue.Green,
        ["info_spawngroup_load_unload"] = GraphHue.Green,
        ["info_target"] = GraphHue.Green,
        ["info_target_advisor_roaming_crash"] = GraphHue.Green,
        ["info_target_gunshipcrash"] = GraphHue.Green,
        ["info_target_helicopter_crash"] = GraphHue.Green,
        ["info_target_vehicle_transition"] = GraphHue.Green,
        ["info_teleporter_countdown"] = GraphHue.Green,
        ["info_visibility_box"] = GraphHue.Orange,
        ["logic_activityevent"] = GraphHue.Cyan,
        ["logic_auto"] = GraphHue.Orange,
        ["logic_branch"] = GraphHue.Cyan,
        ["logic_case"] = GraphHue.Cyan,
        ["logic_compare"] = GraphHue.Cyan,
        ["logic_gamestate_report"] = GraphHue.Cyan,
        ["logic_multicompare"] = GraphHue.Cyan,
        ["logic_relay"] = GraphHue.Blue,
        ["logic_script"] = GraphHue.Green,
        ["logic_timer"] = GraphHue.Orange,
        ["momentary_rot_button"] = GraphHue.Blue,
        ["npc_heli_avoidsphere"] = GraphHue.Orange,
        ["physics_cannister"] = GraphHue.Neutral,
        ["point_commentary_node"] = GraphHue.Orange,
        ["point_instructor_event"] = GraphHue.Teal,
        ["point_nav_walkable"] = GraphHue.Green,
        ["point_script"] = GraphHue.Cyan,
        ["point_template"] = GraphHue.Emerald,
        ["save_photogrammetry_anchor"] = GraphHue.Orange,
        ["sky_camera"] = GraphHue.Teal,
        ["skybox_reference"] = GraphHue.Teal,
        ["tanktrain_ai"] = GraphHue.Orange,
        ["tanktrain_aitarget"] = GraphHue.Orange,
        ["visibility_hint"] = GraphHue.Cyan,
    };
}
