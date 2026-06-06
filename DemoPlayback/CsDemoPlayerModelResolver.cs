namespace ValveResourceFormat.DemoPlayback;

/// <summary>
/// Resolves demo player state to a loadable CS2 character model path with layered fallbacks.
/// </summary>
public sealed class CsDemoPlayerModelResolver
{
    private readonly CsDemoFileAccess fileAccess;
    private readonly CsDemoAgentCatalog agentCatalog;

    public CsDemoPlayerModelResolver(CsDemoFileAccess fileAccess)
    {
        this.fileAccess = fileAccess;
        agentCatalog = new CsDemoAgentCatalog(fileAccess);
    }

    public CsDemoPlayerModelResolution Resolve(CsDemoPlayerState player, string mapName)
    {
        if (TryResolveExactCharacterName(player, out var exactPath))
        {
            return new(exactPath, CsDemoPlayerModelFallbackReason.ExactCharacterName, player.PawnCharacterName);
        }

        if (TryResolveDefIndex(player, out var defIndexPath))
        {
            return new(defIndexPath, CsDemoPlayerModelFallbackReason.DefIndexCatalog, player.PawnCharacterDefIndex.ToString());
        }

        if (ShouldUseMapDefault(player)
            && CsDemoPlayerModelPaths.TryGetMapTeamDefault(mapName, player.Team, out var mapPath))
        {
            if (TryFirstExistingModel([mapPath], out var verifiedMapPath))
            {
                return new(verifiedMapPath, CsDemoPlayerModelFallbackReason.MapTeamDefault, mapName);
            }

            return new(mapPath, CsDemoPlayerModelFallbackReason.MapTeamDefault, $"{mapName} (unverified)");
        }

        var teamDefault = CsDemoPlayerModelPaths.GetTeamDefaultModel(player.Team);
        if (TryFirstExistingModel([teamDefault], out var verifiedTeamDefault))
        {
            return new(verifiedTeamDefault, CsDemoPlayerModelFallbackReason.GlobalTeamDefault, player.Team.ToString());
        }

        if (TryFirstExistingModel([CsDemoPlayerModelPaths.SafeGlobalFallback], out var safePath))
        {
            return new(safePath, CsDemoPlayerModelFallbackReason.SafeGlobalFallback, null);
        }

        return new(teamDefault, CsDemoPlayerModelFallbackReason.GlobalTeamDefault, $"{player.Team} (unverified)");
    }

    private bool TryResolveExactCharacterName(CsDemoPlayerState player, out string modelPath)
    {
        modelPath = string.Empty;

        if (CsDemoPlayerModelPaths.IsMapBasedCharacterName(player.PawnCharacterName))
        {
            return false;
        }

        if (agentCatalog.TryGetModelByName(player.PawnCharacterName, out modelPath)
            && fileAccess.ModelExists(modelPath))
        {
            return true;
        }

        return TryFirstExistingModel(CsDemoPlayerModelPaths.BuildCandidatesFromCharacterName(player.PawnCharacterName), out modelPath);
    }

    private bool TryResolveDefIndex(CsDemoPlayerState player, out string modelPath)
    {
        modelPath = string.Empty;

        if (CsDemoPlayerModelPaths.IsDefaultCharacterDefIndex(player.PawnCharacterDefIndex))
        {
            return false;
        }

        if (!agentCatalog.TryGetModelByDefIndex(player.PawnCharacterDefIndex, out modelPath))
        {
            return false;
        }

        return fileAccess.ModelExists(modelPath);
    }

    private static bool ShouldUseMapDefault(CsDemoPlayerState player)
        => CsDemoPlayerModelPaths.IsMapBasedCharacterName(player.PawnCharacterName)
            || CsDemoPlayerModelPaths.IsDefaultCharacterDefIndex(player.PawnCharacterDefIndex);

    private bool TryFirstExistingModel(IEnumerable<string> candidates, out string modelPath)
    {
        foreach (var candidate in candidates)
        {
            if (fileAccess.ModelExists(candidate))
            {
                modelPath = candidate;
                return true;
            }
        }

        modelPath = string.Empty;
        return false;
    }
}
