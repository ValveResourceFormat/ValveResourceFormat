using GUI.Types.Graphs.Core;

namespace GUI.Types.Graphs;

/// <summary>
/// Entity classname to header hue for the entity I/O graph. Lookup order: exact overrides
/// (classes whose Source 2 Hammer editor icon dictates a color), the reserved sound hue,
/// family prefixes, Neutral. Pink is reserved for sound entities and Emerald for
/// point_template.
/// </summary>
internal static class EntityClassHues
{
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
        ["env_fade"] = GraphHue.Red,
        ["env_fire"] = GraphHue.Orange,
        ["env_firesource"] = GraphHue.Orange,
        ["env_physexplosion"] = GraphHue.Red,
        ["env_physimpact"] = GraphHue.Red,
        ["env_sky"] = GraphHue.Cyan,
        ["env_spark"] = GraphHue.Amber,
        ["env_volumetric_fog_volume"] = GraphHue.Cyan,
        ["env_wind"] = GraphHue.Blue,
        ["fog_volume"] = GraphHue.Cyan,
        ["game_end"] = GraphHue.Red,
        ["game_text"] = GraphHue.Amber,
        ["gibshooter"] = GraphHue.Red,
        ["haptic_relay"] = GraphHue.Red,
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
        ["logic_auto"] = GraphHue.Red,
        ["logic_branch"] = GraphHue.Cyan,
        ["logic_case"] = GraphHue.Cyan,
        ["logic_compare"] = GraphHue.Cyan,
        ["logic_gamestate_report"] = GraphHue.Cyan,
        ["logic_multicompare"] = GraphHue.Cyan,
        ["logic_relay"] = GraphHue.Blue,
        ["logic_script"] = GraphHue.Green,
        ["logic_timer"] = GraphHue.Red,
        ["momentary_rot_button"] = GraphHue.Blue,
        ["npc_heli_avoidsphere"] = GraphHue.Orange,
        ["physics_cannister"] = GraphHue.Neutral,
        ["point_commentary_node"] = GraphHue.Orange,
        ["point_instructor_event"] = GraphHue.Teal,
        ["point_nav_walkable"] = GraphHue.Green,
        ["point_script"] = GraphHue.Cyan,
        ["point_template"] = GraphHue.Emerald,
        ["save_photogrammetry_anchor"] = GraphHue.Red,
        ["sky_camera"] = GraphHue.Teal,
        ["skybox_reference"] = GraphHue.Teal,
        ["tanktrain_ai"] = GraphHue.Orange,
        ["tanktrain_aitarget"] = GraphHue.Red,
        ["visibility_hint"] = GraphHue.Cyan,
    };
}
