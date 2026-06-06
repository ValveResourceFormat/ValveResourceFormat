namespace ValveResourceFormat.DemoPlayback;

/// <summary>
/// Known CS2 player model paths and character-name to VMDL path conversion.
/// </summary>
public static class CsDemoPlayerModelPaths
{
    public const ushort InvalidCharacterDefIndex = ushort.MaxValue;

    public const string CtTeamDefault = "characters/models/ctm_sas/ctm_sas.vmdl";
    public const string TTeamDefault = "characters/models/tm_phoenix/tm_phoenix.vmdl";
    public const string SafeGlobalFallback = "models/dev/error.vmdl";

    private static readonly string[] CtDefaultModels =
    [
        "characters/models/ctm_sas/ctm_sas.vmdl",
        "characters/models/ctm_st6/ctm_st6_variantk.vmdl",
        "characters/models/ctm_st6/ctm_st6_varianti.vmdl",
        "characters/models/ctm_fbi/ctm_fbi.vmdl",
        "characters/models/ctm_fbi/ctm_fbi_variantg.vmdl",
        "characters/models/ctm_gign/ctm_gign.vmdl",
    ];

    private static readonly string[] TDefaultModels =
    [
        "characters/models/tm_phoenix/tm_phoenix.vmdl",
        "characters/models/tm_leet/tm_leet_varianta.vmdl",
        "characters/models/tm_balkan/tm_balkan_varianta.vmdl",
        "characters/models/tm_professional/tm_professional_varf5.vmdl",
        "characters/models/tm_jumpsuit/tm_jumpsuit_varianta.vmdl",
        "characters/models/tm_pirate/tm_pirate.vmdl",
    ];

    private static readonly Dictionary<string, (string Ct, string T)> MapTeamDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de_dust2"] = ("characters/models/ctm_fbi/ctm_fbi.vmdl", "characters/models/tm_leet/tm_leet_varianta.vmdl"),
        ["de_mirage"] = ("characters/models/ctm_sas/ctm_sas.vmdl", "characters/models/tm_leet/tm_leet_varianta.vmdl"),
        ["de_inferno"] = ("characters/models/ctm_gign/ctm_gign.vmdl", "characters/models/tm_balkan/tm_balkan_varianta.vmdl"),
        ["de_nuke"] = ("characters/models/ctm_fbi/ctm_fbi.vmdl", "characters/models/tm_phoenix/tm_phoenix.vmdl"),
        ["de_overpass"] = ("characters/models/ctm_fbi/ctm_fbi.vmdl", "characters/models/tm_phoenix/tm_phoenix.vmdl"),
        ["de_vertigo"] = ("characters/models/ctm_fbi/ctm_fbi.vmdl", "characters/models/tm_phoenix/tm_phoenix.vmdl"),
        ["de_anubis"] = ("characters/models/ctm_fbi/ctm_fbi.vmdl", "characters/models/tm_leet/tm_leet_varianta.vmdl"),
        ["de_ancient"] = ("characters/models/ctm_fbi/ctm_fbi.vmdl", "characters/models/tm_balkan/tm_balkan_varianta.vmdl"),
    };

    public static string GetTeamDefaultModel(CsDemoTeam team)
        => team switch
        {
            CsDemoTeam.Terrorist => TTeamDefault,
            CsDemoTeam.CounterTerrorist => CtTeamDefault,
            _ => CtTeamDefault,
        };

    public static IReadOnlyList<string> GetDefaultModelPool(CsDemoTeam team)
        => team switch
        {
            CsDemoTeam.Terrorist => TDefaultModels,
            CsDemoTeam.CounterTerrorist => CtDefaultModels,
            _ => CtDefaultModels,
        };

    public static bool TryGetMapTeamDefault(string mapName, CsDemoTeam team, out string modelPath)
    {
        var normalizedMapName = NormalizeMapName(mapName);

        if (MapTeamDefaults.TryGetValue(normalizedMapName, out var defaults))
        {
            modelPath = team == CsDemoTeam.Terrorist ? defaults.T : defaults.Ct;
            return true;
        }

        modelPath = string.Empty;
        return false;
    }

    public static IEnumerable<string> BuildCandidatesFromCharacterName(string? pawnCharacterName)
    {
        if (string.IsNullOrWhiteSpace(pawnCharacterName))
        {
            yield break;
        }

        var name = pawnCharacterName;
        if (name.StartsWith("customplayer_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["customplayer_".Length..];
        }

        if (IsMapBasedName(name))
        {
            yield break;
        }

        var faction = GetFactionFolder(name);

        yield return $"agents/models/{faction}/{name}.vmdl";
        yield return $"characters/models/{faction}/{name}.vmdl";
        yield return $"agents/models/{name}.vmdl";
        yield return $"characters/models/{name}.vmdl";
    }

    public static string ToAgentsModelPath(string modelPath)
        => modelPath.Replace("characters/models/", "agents/models/", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<string> GetLoadCandidates(string resolvedModelPath)
    {
        var agentsPath = ToAgentsModelPath(resolvedModelPath);
        if (!agentsPath.Equals(resolvedModelPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return agentsPath;
        }

        yield return resolvedModelPath;
    }

    public static bool IsMapBasedCharacterName(string? pawnCharacterName)
    {
        if (string.IsNullOrWhiteSpace(pawnCharacterName))
        {
            return true;
        }

        var name = pawnCharacterName;
        if (name.StartsWith("customplayer_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["customplayer_".Length..];
        }

        return IsMapBasedName(name);
    }

    public static bool IsDefaultCharacterDefIndex(ushort pawnCharacterDefIndex)
        => pawnCharacterDefIndex == InvalidCharacterDefIndex || pawnCharacterDefIndex == 0;

    private static bool IsMapBasedName(string name)
        => name.Equals("ct_map_based", StringComparison.OrdinalIgnoreCase)
            || name.Equals("tm_map_based", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("_map_based", StringComparison.OrdinalIgnoreCase);

    private static string GetFactionFolder(string name)
    {
        var variantIndex = name.IndexOf("_variant", StringComparison.OrdinalIgnoreCase);
        if (variantIndex > 0)
        {
            return name[..variantIndex];
        }

        var varIndex = name.IndexOf("_var", StringComparison.OrdinalIgnoreCase);
        if (varIndex > 0)
        {
            return name[..varIndex];
        }

        return name;
    }

    private static string NormalizeMapName(string mapName)
    {
        var normalized = mapName.Replace('\\', '/').Trim('/');

        if (normalized.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["maps/".Length..];
        }

        if (normalized.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        if (normalized.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }

        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        return normalized;
    }
}
