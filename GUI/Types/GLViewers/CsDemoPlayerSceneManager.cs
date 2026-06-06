using System.Globalization;
using System.Linq;
using GUI.Utils;
using Microsoft.Extensions.Logging;
using ValveResourceFormat;
using ValveResourceFormat.DemoPlayback;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers;

sealed class CsDemoPlayerSceneManager : IDisposable
{
    private const string DemoLayerName = "Demo Playback";

    private readonly Scene scene;
    private readonly VrfGuiContext guiContext;
    private readonly CsDemoPlayerModelResolver resolver;
    private readonly string mapName;
    private readonly ILogger logger;
    private readonly bool debugLogging;
    private readonly Dictionary<string, Model?> modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, (SceneNode Node, string Signature)> playerNodes = [];
    private readonly Dictionary<int, string> defaultModelAssignments = [];
    private readonly Dictionary<int, (Vector3 Position, int Tick, bool OnGround)> previousPlayerStates = [];
    private readonly Dictionary<int, int> previousShotsFired = [];
    private readonly Dictionary<int, string> previousActiveWeapons = [];
    private readonly Dictionary<int, HashSet<uint>> knownGrenadeProjectiles = [];
    private readonly Dictionary<int, (Vector3 Position, int Tick, bool OnGround)> debugPreviousPlayerStates = [];
    private readonly Dictionary<int, int> debugPreviousShotsFired = [];
    private readonly Dictionary<int, CsDemoPlayerAnimDebugNodeStatus> animDebugNodeStatuses = [];
    private readonly HashSet<string> loggedSignatures = new(StringComparer.Ordinal);
    private readonly HashSet<string> loggedAnimationSignatures = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> agentDebugAnimationSignatures = [];

    public CsDemoPlayerSceneManager(
        Scene scene,
        VrfGuiContext guiContext,
        string mapName,
        ILogger logger)
    {
        this.scene = scene;
        this.guiContext = guiContext;
        this.mapName = mapName;
        this.logger = logger;
        debugLogging = string.Equals(
            Environment.GetEnvironmentVariable("VRF_DEMO_PLAYER_MODEL_DEBUG"),
            "1",
            StringComparison.Ordinal);
        resolver = new CsDemoPlayerModelResolver(new CsDemoFileAccess(guiContext));
    }

    public void LogSeekState(int frameTick)
    {
        // #region agent log
        AgentDebugLog.Write(
            "H1",
            "GUI/Types/GLViewers/CsDemoPlayerSceneManager.cs:LogSeekState",
            "seek apply with retained per-player animation dictionaries",
            new
            {
                frameTick,
                playerNodeCount = playerNodes.Count,
                previousWeaponCount = previousActiveWeapons.Count,
                previousShotsCount = previousShotsFired.Count,
                previousStateCount = previousPlayerStates.Count,
                retainedSlots = previousActiveWeapons.Keys.Order().ToArray(),
            });
        // #endregion
    }

    public void ApplyFrame(CsDemoFrame frame, bool isPlaying)
    {
        var activeSlots = new HashSet<int>();
        UpdateDefaultModelAssignments(frame);

        foreach (var player in frame.Players.Where(static player => player.IsAlive))
        {
            activeSlots.Add(player.Slot);

            var signature = BuildSignature(player);
            if (!playerNodes.TryGetValue(player.Slot, out var existing) || existing.Signature != signature)
            {
                // #region agent log
                AgentDebugLog.Write(
                    "H3",
                    "GUI/Types/GLViewers/CsDemoPlayerSceneManager.cs:RecreatePlayerNode",
                    "player model node recreated",
                    new
                    {
                        slot = player.Slot,
                        tick = frame.Tick,
                        signature,
                        hadExisting = playerNodes.ContainsKey(player.Slot),
                        previousSignature = playerNodes.TryGetValue(player.Slot, out var prior) ? prior.Signature : null,
                    });
                // #endregion

                if (existing.Node != null)
                {
                    existing.Node.Delete();
                    scene.Remove(existing.Node, dynamic: true);
                }

                existing = (CreatePlayerNode(player), signature);
                scene.Add(existing.Node, dynamic: true);
                playerNodes[player.Slot] = existing;
                LogPlayerModel(player, signature);
            }

            SetPlayerTransform(existing.Node, player);
            UpdatePlayerAnimation(existing.Node, player, frame, frame.Tick, isPlaying);
            existing.Node.LayerEnabled = true;
        }

        foreach (var slot in playerNodes.Keys.Except(activeSlots).ToArray())
        {
            var existing = playerNodes[slot];
            existing.Node.Delete();
            scene.Remove(existing.Node, dynamic: true);
            playerNodes.Remove(slot);
            previousPlayerStates.Remove(slot);
            previousShotsFired.Remove(slot);
            previousActiveWeapons.Remove(slot);
            knownGrenadeProjectiles.Remove(slot);
            debugPreviousPlayerStates.Remove(slot);
            debugPreviousShotsFired.Remove(slot);
            animDebugNodeStatuses.Remove(slot);
            defaultModelAssignments.Remove(slot);
        }
    }

    public bool TryGetNode(int slot, out SceneNode node)
    {
        if (playerNodes.TryGetValue(slot, out var existing))
        {
            node = existing.Node;
            return true;
        }

        node = null!;
        return false;
    }

    public bool TryGetAnimDebugNodeStatus(int slot, out CsDemoPlayerAnimDebugNodeStatus status)
        => animDebugNodeStatuses.TryGetValue(slot, out status);

    public bool TryGetPreviousOnGround(int slot, out bool onGround)
    {
        if (debugPreviousPlayerStates.TryGetValue(slot, out var previous))
        {
            onGround = previous.OnGround;
            return true;
        }

        onGround = default;
        return false;
    }

    public bool TryGetPreviousShotsFired(int slot, out int shotsFired)
        => debugPreviousShotsFired.TryGetValue(slot, out shotsFired);

    public IReadOnlyList<PlayerAnimDebugData> GetAnimDebugData(
        CsDemoFrame frame,
        Vector3 cameraLocation,
        ulong? selectedSteamId)
    {
        var results = new List<PlayerAnimDebugData>();

        foreach (var player in frame.Players.Where(static player => player.IsAlive))
        {
            debugPreviousPlayerStates.TryGetValue(player.Slot, out var debugPrevious);
            var animationState = CsDemoPlayerAnimationState.FromPlayer(
                player,
                frame.Tick,
                debugPreviousPlayerStates.ContainsKey(player.Slot) ? debugPrevious : null);

            animDebugNodeStatuses.TryGetValue(player.Slot, out var nodeStatus);
            bool? previousOnGround = TryGetPreviousOnGround(player.Slot, out var prevGround) ? prevGround : null;
            int? previousShots = TryGetPreviousShotsFired(player.Slot, out var prevShots) ? prevShots : null;

            var isSelected = selectedSteamId == player.SteamId;
            var distance = Vector3.Distance(cameraLocation, player.Position);
            var detailLevel = CsDemoAnimDebugOverlay.ResolveDetailLevel(distance, isSelected);

            results.Add(CsDemoPlayerAnimDebugResolver.Resolve(
                player,
                animationState,
                nodeStatus,
                previousOnGround,
                previousShots,
                player.Position,
                isSelected,
                detailLevel));
        }

        return results;
    }

    public void Clear()
    {
        foreach (var existing in playerNodes.Values)
        {
            existing.Node.Delete();
            scene.Remove(existing.Node, dynamic: true);
        }

        playerNodes.Clear();
        defaultModelAssignments.Clear();
        previousPlayerStates.Clear();
        previousShotsFired.Clear();
        previousActiveWeapons.Clear();
        knownGrenadeProjectiles.Clear();
        debugPreviousPlayerStates.Clear();
        debugPreviousShotsFired.Clear();
        animDebugNodeStatuses.Clear();
        loggedAnimationSignatures.Clear();
        agentDebugAnimationSignatures.Clear();
    }

    public void Dispose()
    {
        Clear();
    }

    private SceneNode CreatePlayerNode(CsDemoPlayerState player)
    {
        var resolution = resolver.Resolve(player, mapName);
        var resolvedModelPath = ResolveAssignedModelPath(player, resolution);

        if (resolution.FallbackReason != CsDemoPlayerModelFallbackReason.BoxProxyFallback)
        {
            foreach (var modelPath in CsDemoPlayerModelPaths.GetLoadCandidates(resolvedModelPath))
            {
                if (IsErrorModelPath(modelPath))
                {
                    continue;
                }

                if (TryCreateModelNode(player, resolution, modelPath) is { } modelNode)
                {
                    return modelNode;
                }
            }
        }

        foreach (var fallbackModelPath in GetFallbackModelPaths(player.Team))
        {
            if (TryCreateModelNode(player, resolution, fallbackModelPath) is { } fallbackModelNode)
            {
                return fallbackModelNode;
            }
        }

        LogLoadResult(player, resolution, loaded: false, modelNode: null, loadedPath: resolvedModelPath);

        animDebugNodeStatuses[player.Slot] = CreateBoxProxyStatus();
        return new SimpleBoxSceneNode(scene, TeamColor(player.Team), new Vector3(32, 32, 72))
        {
            LayerName = DemoLayerName,
            Name = player.Name,
            LayerEnabled = true,
        };
    }

    private static CsDemoPlayerAnimDebugNodeStatus CreateBoxProxyStatus()
        => new(MissingModel: true, FallbackAnim: false, MissingSkeleton: false, ClipLabel: null);

    private ModelSceneNode? TryCreateModelNode(
        CsDemoPlayerState player,
        CsDemoPlayerModelResolution resolution,
        string modelPath)
    {
        if (LoadModel(modelPath) is not { } model)
        {
            return null;
        }

        var modelNode = new ModelSceneNode(scene, model, isWorldPreview: false)
        {
            LayerName = DemoLayerName,
            Name = player.Name,
            LayerEnabled = true,
            Flags = ObjectTypeFlags.DisableVisCulling,
        };

        if (!modelNode.HasMeshes)
        {
            modelNode.Delete();
            return null;
        }

        if (!CsDemoPlayerModelSetup.TryConfigureThirdPersonModel(modelNode, model, modelPath))
        {
            if (debugLogging)
            {
                logger.LogInformation(
                    "[slot={Slot}] path={Path} meshGroups=[{Groups}] renderable=0",
                    player.Slot,
                    modelPath,
                    string.Join(", ", modelNode.GetMeshGroups()));
            }

            modelNode.Delete();
            return null;
        }

        CsDemoPlayerModelSetup.ApplyThirdPersonIdlePose(modelNode);
        modelNode.SetCharacterEyeRenderParams();
        LogLoadResult(player, resolution, loaded: true, modelNode, modelPath);
        animDebugNodeStatuses[player.Slot] = CreateModelNodeStatus(modelNode, CsDemoPlayerWeaponAnimationGroup.FromWeapon(player.ActiveWeapon));
        return modelNode;
    }

    private Model? LoadModel(string path)
    {
        if (modelCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var resource = guiContext.LoadFileCompiled(path);
        cached = resource?.DataBlock as Model;
        modelCache[path] = cached;

        return cached;
    }

    private static IEnumerable<string> GetFallbackModelPaths(CsDemoTeam team)
    {
        foreach (var defaultModelPath in CsDemoPlayerModelPaths.GetDefaultModelPool(team))
        {
            foreach (var candidate in CsDemoPlayerModelPaths.GetLoadCandidates(defaultModelPath))
            {
                yield return candidate;
            }
        }

        foreach (var candidate in CsDemoPlayerModelPaths.GetLoadCandidates(CsDemoPlayerModelPaths.GetTeamDefaultModel(team)))
        {
            yield return candidate;
        }
    }

    private static bool IsErrorModelPath(string modelPath)
        => modelPath.Equals(CsDemoPlayerModelPaths.SafeGlobalFallback, StringComparison.OrdinalIgnoreCase);

    private void UpdateDefaultModelAssignments(CsDemoFrame frame)
    {
        foreach (var teamGroup in frame.Players
            .Where(static player => player.IsAlive)
            .GroupBy(static player => player.Team))
        {
            var pool = CsDemoPlayerModelPaths.GetDefaultModelPool(teamGroup.Key);
            var defaultPlayers = teamGroup
                .Where(IsDefaultModelPlayer)
                .OrderBy(static player => player.Slot)
                .ToArray();

            for (var i = 0; i < defaultPlayers.Length; i++)
            {
                defaultModelAssignments[defaultPlayers[i].Slot] = pool[i % pool.Count];
            }
        }
    }

    private string ResolveAssignedModelPath(CsDemoPlayerState player, CsDemoPlayerModelResolution resolution)
        => IsDefaultModelPlayer(player) && defaultModelAssignments.TryGetValue(player.Slot, out var modelPath)
            ? modelPath
            : resolution.ResolvedModelPath;

    private static bool IsDefaultModelPlayer(CsDemoPlayerState player)
        => CsDemoPlayerModelPaths.IsMapBasedCharacterName(player.PawnCharacterName)
            || CsDemoPlayerModelPaths.IsDefaultCharacterDefIndex(player.PawnCharacterDefIndex);

    private static void SetPlayerTransform(SceneNode node, CsDemoPlayerState player)
    {
        var transform = Matrix4x4.CreateRotationZ(float.DegreesToRadians(player.Yaw))
            * Matrix4x4.CreateTranslation(player.Position);

        node.Transform = transform;
        node.LightingOrigin = player.Position;
        node.Scene.MarkParentOctreeDirty(node);
    }

    private void UpdatePlayerAnimation(SceneNode node, CsDemoPlayerState player, CsDemoFrame frame, int tick, bool isPlaying)
    {
        if (node is not ModelSceneNode modelNode)
        {
            return;
        }

        previousPlayerStates.TryGetValue(player.Slot, out var previous);
        var hasPrevious = previousPlayerStates.ContainsKey(player.Slot);
        if (hasPrevious)
        {
            debugPreviousPlayerStates[player.Slot] = previous;
        }

        previousShotsFired.TryGetValue(player.Slot, out var previousShots);
        if (previousShotsFired.ContainsKey(player.Slot))
        {
            debugPreviousShotsFired[player.Slot] = previousShots;
        }

        previousActiveWeapons.TryGetValue(player.Slot, out var previousWeapon);
        var weaponChanged = previousActiveWeapons.ContainsKey(player.Slot)
            && !string.Equals(previousWeapon, player.ActiveWeapon, StringComparison.Ordinal);
        var firedShot = player.ShotsFired > previousShots;
        var threwGrenade = DetectGrenadeThrow(player, frame);

        if (!isPlaying || weaponChanged || firedShot || threwGrenade)
        {
            // #region agent log
            AgentDebugLog.Write(
                "H1,H7,H8",
                "GUI/Types/GLViewers/CsDemoPlayerSceneManager.cs:UpdatePlayerAnimationInputs",
                "animation input snapshot",
                new
                {
                    tick,
                    slot = player.Slot,
                    isPlaying,
                    activeWeapon = player.ActiveWeapon,
                    previousWeapon = previousActiveWeapons.TryGetValue(player.Slot, out var pw) ? pw : null,
                    hasPreviousWeapon = previousActiveWeapons.ContainsKey(player.Slot),
                    weaponChanged,
                    shotsFired = player.ShotsFired,
                    previousShots,
                    firedShot,
                    threwGrenade,
                    animEventCount = frame.PlayerAnimEvents.Count(e => e.Slot == player.Slot),
                    activeAnimation = modelNode.AnimationController.ActiveAnimation?.Name,
                    isPaused = modelNode.AnimationController.IsPaused,
                });
            // #endregion
        }

        var animationState = CsDemoPlayerAnimationState.FromPlayer(
            player,
            tick,
            hasPrevious ? previous : null);

        previousPlayerStates[player.Slot] = (player.Position, tick, animationState.OnGround);
        previousShotsFired[player.Slot] = player.ShotsFired;
        previousActiveWeapons[player.Slot] = player.ActiveWeapon ?? string.Empty;
        LogPlayerAnimationState(player, animationState);
        CsDemoPlayerModelSetup.ApplyThirdPersonMovement(
            modelNode,
            animationState.Velocity,
            player.Yaw,
            isPlaying,
            animationState.OnGround,
            animationState.JustJumped,
            animationState.CrouchBlend,
            animationState.IsWalking,
            animationState.MovementInput,
            animationState.WeaponGroup);
        var playerAnimEvents = frame.PlayerAnimEvents
            .Where(animEvent => animEvent.Slot == player.Slot)
            .ToArray();

        CsDemoPlayerWeaponAnimation.Apply(
            modelNode,
            animationState.WeaponGroup,
            weaponChanged,
            firedShot,
            threwGrenade,
            playerAnimEvents,
            isPlaying);

        LogCollapsedPoseIfNeeded(modelNode, player, frame.Tick, animationState);
        animDebugNodeStatuses[player.Slot] = CreateModelNodeStatus(modelNode, animationState.WeaponGroup);

        var debugSignature = string.Create(
            CultureInfo.InvariantCulture,
            $"{player.ActiveWeapon}:{animationState.WeaponGroup.Item}:{animationState.OnGround}:{animationState.JustJumped}:{animationState.CrouchBlend:0.0}:{animationState.IsWalking}:{animationState.Velocity.Length() / 25f:0}:{modelNode.AnimationController.ActiveAnimation?.Name}:{modelNode.AnimationController.Frame}:{modelNode.AnimationController.IsPaused}");
        if (!agentDebugAnimationSignatures.TryGetValue(player.Slot, out var lastDebugSignature) || lastDebugSignature != debugSignature)
        {
            agentDebugAnimationSignatures[player.Slot] = debugSignature;
            var status = animDebugNodeStatuses[player.Slot];
            // #region agent log
            AgentDebugLog.Write(
                "H2,H3,H4,H5",
                "GUI/Types/GLViewers/CsDemoPlayerSceneManager.cs:UpdatePlayerAnimation",
                "player animation update result",
                new
                {
                    tick,
                    slot = player.Slot,
                    team = player.Team.ToString(),
                    activeWeapon = player.ActiveWeapon,
                    weaponGroup = animationState.WeaponGroup.Item,
                    defaultWeaponGroup = animationState.WeaponGroup.DefaultItem,
                    velocityX = animationState.Velocity.X,
                    velocityY = animationState.Velocity.Y,
                    velocityZ = animationState.Velocity.Z,
                    speed = animationState.Velocity.Length(),
                    onGround = animationState.OnGround,
                    justJumped = animationState.JustJumped,
                    crouchBlend = animationState.CrouchBlend,
                    isWalking = animationState.IsWalking,
                    movementInputX = animationState.MovementInput.X,
                    movementInputY = animationState.MovementInput.Y,
                    activeAnimation = modelNode.AnimationController.ActiveAnimation?.Name,
                    activeClipFinished = modelNode.AnimationController.ActiveClipFinished,
                    frame = modelNode.AnimationController.Frame,
                    time = modelNode.AnimationController.Time,
                    isPaused = modelNode.AnimationController.IsPaused,
                    animationCount = modelNode.Animations.Count,
                    missingModel = status.MissingModel,
                    missingSkeleton = status.MissingSkeleton,
                    fallbackAnim = status.FallbackAnim,
                    clipLabel = status.ClipLabel,
                });
            // #endregion
        }
    }

    private static void LogCollapsedPoseIfNeeded(
        ModelSceneNode modelNode,
        CsDemoPlayerState player,
        int tick,
        CsDemoPlayerAnimationState animationState)
    {
        if (!TryGetPoseSize(modelNode.AnimationController.Pose, out var poseSize)
            || !TryGetPoseSize(modelNode.AnimationController.BindPose, out var bindPoseSize)
            || bindPoseSize.Z <= 0f)
        {
            return;
        }

        var minHeightRatio = animationState.CrouchBlend > 0.5f ? 0.45f : 0.7f;
        var heightRatio = poseSize.Z / bindPoseSize.Z;
        if (heightRatio >= minHeightRatio)
        {
            return;
        }

        AgentDebugLog.Write(
            "WATCHDOG_FAIL,H14",
            "GUI/Types/GLViewers/CsDemoPlayerSceneManager.cs:LogCollapsedPoseIfNeeded",
            "WATCHDOG_FAIL pose issue: player skeleton collapsed",
            new
            {
                tick,
                slot = player.Slot,
                activeWeapon = player.ActiveWeapon,
                weaponGroup = animationState.WeaponGroup.Item,
                defaultWeaponGroup = animationState.WeaponGroup.DefaultItem,
                poseSizeX = poseSize.X,
                poseSizeY = poseSize.Y,
                poseSizeZ = poseSize.Z,
                bindPoseSizeX = bindPoseSize.X,
                bindPoseSizeY = bindPoseSize.Y,
                bindPoseSizeZ = bindPoseSize.Z,
                heightRatio,
                minHeightRatio,
                activeAnimation = modelNode.AnimationController.ActiveAnimation?.Name,
                dominantClip = modelNode.AnimationController.GetDominantClipName(),
                frame = modelNode.AnimationController.Frame,
                time = modelNode.AnimationController.Time,
            });
    }

    private static bool TryGetPoseSize(IReadOnlyList<Matrix4x4> pose, out Vector3 size)
    {
        if (pose.Count == 0)
        {
            size = default;
            return false;
        }

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        var count = 0;

        foreach (var matrix in pose)
        {
            var position = matrix.Translation;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            {
                size = default;
                return false;
            }

            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
            count++;
        }

        size = max - min;
        return count > 0;
    }

    private bool DetectGrenadeThrow(CsDemoPlayerState player, CsDemoFrame frame)
    {
        if (!knownGrenadeProjectiles.TryGetValue(player.Slot, out var known))
        {
            known = [];
            knownGrenadeProjectiles[player.Slot] = known;
        }

        foreach (var entity in frame.WorldEntities)
        {
            if (entity.Kind != CsDemoWorldEntityKind.GrenadeProjectile)
            {
                continue;
            }

            if (string.IsNullOrEmpty(entity.OwnerName)
                || !entity.OwnerName.Equals(player.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Vector3.Distance(entity.Position, player.Position) > 256f)
            {
                continue;
            }

            if (known.Add(entity.EntityIndex))
            {
                return true;
            }
        }

        return false;
    }

    private static CsDemoPlayerAnimDebugNodeStatus CreateModelNodeStatus(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup weaponGroup)
    {
        var clipPath = modelNode.AnimationController.GetDominantClipName()
            ?? modelNode.AnimationController.ActiveAnimation?.Name;
        return new CsDemoPlayerAnimDebugNodeStatus(
            MissingModel: false,
            FallbackAnim: CsDemoPlayerModelSetup.UsesWeaponAnimFallback(modelNode, weaponGroup),
            MissingSkeleton: modelNode.Animations.Count == 0,
            ClipLabel: CsDemoPlayerAnimDebugResolver.ShortClipLabel(clipPath));
    }

    private void LogPlayerAnimationState(CsDemoPlayerState player, CsDemoPlayerAnimationState animationState)
    {
        if (!debugLogging)
        {
            return;
        }

        var signature = string.Create(
            CultureInfo.InvariantCulture,
            $"{player.Slot}:{player.ActiveWeapon}:{animationState.WeaponGroup.Item}:{player.OnGround}:{player.CrouchBlend > 0f}:{player.IsWalking}:{player.IsScoped}:{player.IsDefusing}");
        if (!loggedAnimationSignatures.Add(signature))
        {
            return;
        }

        logger.LogInformation(
            "[slot={Slot}] anim weapon={Weapon} group={Group}/{DefaultGroup} velocity=({VelocityX:F1},{VelocityY:F1},{VelocityZ:F1}) onGround={OnGround} crouch={Crouch:F2} walking={Walking} scoped={Scoped} defusing={Defusing} input=({InputX:F1},{InputY:F1})",
            player.Slot,
            player.ActiveWeapon ?? "(none)",
            animationState.WeaponGroup.Item,
            animationState.WeaponGroup.DefaultItem,
            animationState.Velocity.X,
            animationState.Velocity.Y,
            animationState.Velocity.Z,
            animationState.OnGround,
            animationState.CrouchBlend,
            animationState.IsWalking,
            player.IsScoped,
            player.IsDefusing,
            animationState.MovementInput.X,
            animationState.MovementInput.Y);
    }

    private string BuildSignature(CsDemoPlayerState player)
    {
        var resolution = resolver.Resolve(player, mapName);
        return $"{player.Team}:{player.PawnCharacterName}:{player.PawnCharacterDefIndex}:{ResolveAssignedModelPath(player, resolution)}";
    }

    private static Color32 TeamColor(CsDemoTeam team)
        => team switch
        {
            CsDemoTeam.Terrorist => new Color32(226, 152, 52),
            CsDemoTeam.CounterTerrorist => new Color32(70, 150, 230),
            CsDemoTeam.Spectator => new Color32(160, 160, 160),
            _ => new Color32(220, 220, 220),
        };

    private void LogPlayerModel(CsDemoPlayerState player, string signature)
    {
        if (!debugLogging || loggedSignatures.Contains(signature))
        {
            return;
        }

        loggedSignatures.Add(signature);

        var resolution = resolver.Resolve(player, mapName);
        logger.LogInformation(
            "[slot={Slot}] name={Name} team={Team} charName={CharacterName} defIndex={DefIndex} -> {ModelPath} ({Reason})",
            player.Slot,
            player.Name,
            player.Team,
            player.PawnCharacterName ?? "(null)",
            player.PawnCharacterDefIndex,
            resolution.ResolvedModelPath,
            resolution.FallbackReason);
    }

    private void LogLoadResult(
        CsDemoPlayerState player,
        CsDemoPlayerModelResolution resolution,
        bool loaded,
        ModelSceneNode? modelNode,
        string? loadedPath)
    {
        // #region agent log
        AgentDebugLog.Write(
            "H4,H5",
            "GUI/Types/GLViewers/CsDemoPlayerSceneManager.cs:LogLoadResult",
            "player model load result",
            new
            {
                slot = player.Slot,
                team = player.Team.ToString(),
                loaded,
                loadedPath = loadedPath ?? resolution.ResolvedModelPath,
                fallbackReason = resolution.FallbackReason.ToString(),
                renderableMeshes = modelNode?.RenderableMeshes.Count ?? 0,
                hasMeshes = modelNode?.HasMeshes ?? false,
                animationCount = modelNode?.Animations.Count ?? 0,
                activeAnimation = modelNode?.AnimationController.ActiveAnimation?.Name,
                detail = resolution.Detail,
            });
        // #endregion

        if (!debugLogging)
        {
            return;
        }

        var animationName = modelNode?.AnimationController.ActiveAnimation?.Name ?? "(bind pose)";
        var meshCount = modelNode?.RenderableMeshes.Count ?? 0;

        logger.LogInformation(
            "[slot={Slot}] loaded={Loaded} path={Path} renderableMeshes={Meshes} anim={Animation} fallback={Reason} detail={Detail}",
            player.Slot,
            loaded,
            loadedPath ?? resolution.ResolvedModelPath,
            meshCount,
            animationName,
            resolution.FallbackReason,
            resolution.Detail ?? "(none)");
    }
}
