using System.Linq;
using System.Runtime.CompilerServices;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers;

static class CsDemoPlayerModelSetup
{
    private static readonly ConditionalWeakTable<ModelSceneNode, HashSet<string>> RegisteredWeaponGroups = new();
    private static readonly ConditionalWeakTable<ModelSceneNode, HashSet<string>> LoggedWeaponGroupResolutions = new();
    private static readonly ConditionalWeakTable<ModelSceneNode, MovementDebugState> MovementDebugStates = new();
    private static readonly ConditionalWeakTable<ModelSceneNode, HashSet<string>> WeaponActionMaskRegistered = new();
    private static readonly CsDemoPlayerWeaponAnimationGroup RifleWeaponGroup = CsDemoPlayerWeaponAnimationGroup.Rifle;
    private static readonly string ThirdPersonIdleClip = GetThirdpersonAnim(RifleWeaponGroup, Posture.Standing, MovementState.Stopped);
    private static readonly string CrouchIdleClip = GetThirdpersonAnim(RifleWeaponGroup, Posture.Crouching, MovementState.Stopped);
    internal const string WeaponActionBoneMask = "WeaponAction";

    private const string BreathingClip = "animation/anims/world/shared/breathing.vnmclip";

    private enum Posture
    {
        Standing,
        Crouching,
    }

    private enum MovementState
    {
        Stopped,
        Walking,
        Running,
        Jumping,
    }

    private enum Heading
    {
        North,
        South,
        East,
        West,
        NorthEast,
        SouthEast,
        SouthWest,
        NorthWest,
    }

    private static readonly Dictionary<Heading, Vector2> HeadingVectors = new()
    {
        [Heading.North] = new(0, 1),
        [Heading.NorthEast] = Vector2.Normalize(new(1, 1)),
        [Heading.East] = new(1, 0),
        [Heading.SouthEast] = Vector2.Normalize(new(1, -1)),
        [Heading.South] = new(0, -1),
        [Heading.SouthWest] = Vector2.Normalize(new(-1, -1)),
        [Heading.West] = new(-1, 0),
        [Heading.NorthWest] = Vector2.Normalize(new(-1, 1)),
    };

    private sealed class MovementDebugState
    {
        public string LastSignature { get; set; } = string.Empty;
    }

    public static bool TryConfigureThirdPersonModel(ModelSceneNode modelNode, Model model, string modelPath)
    {
        // Walk-mode Legs use the model default mesh groups, not thirdperson_default only.
        modelNode.SetActiveMeshGroups(model.GetDefaultMeshGroups());
        if (modelNode.RenderableMeshes.Count > 0)
        {
            TryApplyVariantMaterialGroup(modelNode, model, modelPath);
            // #region agent log
            AgentDebugLog.Write(
                "H6",
                "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:TryConfigureThirdPersonModel",
                "model mesh groups configured",
                new
                {
                    modelPath,
                    meshGroupCount = modelNode.GetMeshGroups().Count(),
                    renderableMeshCount = modelNode.RenderableMeshes.Count,
                    usesDefaultMeshGroups = true,
                    hasFirstPersonGroup = modelNode.GetMeshGroups().Any(static g => g.Contains("firstperson", StringComparison.OrdinalIgnoreCase)),
                });
            // #endregion
            return true;
        }

        var nonFirstPerson = modelNode.GetMeshGroups()
            .Where(static group => !group.Contains("firstperson", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (nonFirstPerson.Length > 0)
        {
            modelNode.SetActiveMeshGroups(nonFirstPerson);
            if (modelNode.RenderableMeshes.Count > 0)
            {
                TryApplyVariantMaterialGroup(modelNode, model, modelPath);
                return true;
            }
        }

        modelNode.SetActiveMeshGroups(model.GetDefaultMeshGroups());
        if (modelNode.RenderableMeshes.Count > 0)
        {
            TryApplyVariantMaterialGroup(modelNode, model, modelPath);
        }

        return modelNode.RenderableMeshes.Count > 0;
    }

    public static void TryApplyVariantMaterialGroup(ModelSceneNode modelNode, Model model, string modelPath)
    {
        var materialGroups = model.GetMaterialGroups().ToArray();
        if (materialGroups.Length <= 1)
        {
            return;
        }

        var fileName = modelPath.Replace('\\', '/');
        var slashIndex = fileName.LastIndexOf('/');
        if (slashIndex >= 0)
        {
            fileName = fileName[(slashIndex + 1)..];
        }

        if (fileName.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^5];
        }
        var candidates = new List<string> { fileName };

        var variantIndex = fileName.IndexOf("_variant", StringComparison.OrdinalIgnoreCase);
        if (variantIndex >= 0)
        {
            candidates.Add(fileName[variantIndex..]);
            candidates.Add(fileName[(variantIndex + "_variant".Length)..]);
        }

        var varIndex = fileName.IndexOf("_var", StringComparison.OrdinalIgnoreCase);
        if (varIndex >= 0)
        {
            candidates.Add(fileName[varIndex..]);
            candidates.Add(fileName[(varIndex + "_var".Length)..]);
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var match = materialGroups.FirstOrDefault(group =>
                group.Name.Contains(candidate, StringComparison.OrdinalIgnoreCase));

            if (match.Name != null)
            {
                modelNode.SetMaterialGroup(match.Name);
                return;
            }
        }
    }

    public static void ApplyThirdPersonIdlePose(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup? weaponGroup = null,
        bool crouching = false)
    {
        modelNode.AnimationController.TwistConstraints = [];
        modelNode.AnimationController.Looping = true;
        modelNode.AnimationController.IsPaused = true;

        var activeWeaponGroup = ResolveAvailableWeaponGroup(modelNode, weaponGroup ?? RifleWeaponGroup);

        var clip = GetThirdpersonAnim(
            activeWeaponGroup,
            crouching ? Posture.Crouching : Posture.Standing,
            MovementState.Stopped);
        if (modelNode.Animations.ContainsKey(clip))
        {
            modelNode.AnimationController.SetAnimationWeight(clip, 1f);
            return;
        }

        foreach (var animationName in new[] { "idle", "idle_lower", "stand_idle", "neutral" })
        {
            if (modelNode.SetAnimationForWorldPreview(animationName))
            {
                modelNode.AnimationController.IsPaused = true;
                return;
            }
        }

        if (modelNode.Animations.Count > 0)
        {
            modelNode.SetAnimation(modelNode.Animations.Values.First());
            modelNode.AnimationController.IsPaused = true;
        }
    }

    public static void ApplyThirdPersonMovement(
        ModelSceneNode modelNode,
        Vector3 velocity,
        float yawDegrees,
        bool isPlaying,
        bool onGround,
        bool justJumped,
        float crouchBlend,
        bool isWalking,
        Vector2 movementInput,
        CsDemoPlayerWeaponAnimationGroup weaponGroup)
    {
        weaponGroup = ResolveAvailableWeaponGroup(modelNode, weaponGroup);
        var idleClip = GetThirdpersonAnim(weaponGroup, Posture.Standing, MovementState.Stopped);

        if (!modelNode.Animations.ContainsKey(idleClip))
        {
            // #region agent log
            AgentDebugLog.Write(
                "H4",
                "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:ApplyThirdPersonMovementMissingIdle",
                "missing idle clip blocks movement animation",
                new
                {
                    requestedWeapon = weaponGroup.Item,
                    defaultWeapon = weaponGroup.DefaultItem,
                    idleClip,
                    animationCount = modelNode.Animations.Count,
                    activeAnimation = modelNode.AnimationController.ActiveAnimation?.Name,
                });
            // #endregion
            return;
        }

        modelNode.AnimationController.IsPaused = !isPlaying;

        if (!isPlaying)
        {
            // #region agent log
            AgentDebugLog.Write(
                "H8",
                "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:ApplyThirdPersonMovementPaused",
                "playback paused before movement weights update",
                new
                {
                    requestedWeapon = weaponGroup.Item,
                    idleClip,
                    activeAnimation = modelNode.AnimationController.ActiveAnimation?.Name,
                    frame = modelNode.AnimationController.Frame,
                    isPaused = modelNode.AnimationController.IsPaused,
                });
            // #endregion
            return;
        }

        var velocity2D = new Vector2(velocity.X, velocity.Y);
        var speed2D = velocity2D.Length();
        crouchBlend = MathUtils.Saturate(crouchBlend);
        var standing = 1f - crouchBlend;

        Vector2 walkRun = new(float.Lerp(84f, 120f, standing), 250f);
        var running = MathUtils.Saturate((speed2D - walkRun.X) / (walkRun.Y - walkRun.X));
        var walking = MathUtils.Saturate(speed2D / walkRun.X) * (1f - running);
        if (isWalking)
        {
            walking = MathUtils.Saturate(speed2D / walkRun.X);
            running = 0f;
        }

        var stopped = MathF.Max(0f, 1f - running - walking);
        var jumping = 0f;
        var crouchWalkTimeScale = GetLocomotionTimeScale(speed2D, 84f);
        var walkTimeScale = GetLocomotionTimeScale(speed2D, 120f);
        var runTimeScale = GetLocomotionTimeScale(speed2D, 250f);

        var jumpStandingClip = GetThirdpersonAnim(weaponGroup, Posture.Standing, MovementState.Jumping);
        var jumpCrouchingClip = GetThirdpersonAnim(weaponGroup, Posture.Crouching, MovementState.Jumping);

        if (!onGround)
        {
            jumping = 1f;
            running = 0f;
            walking = 0f;
            stopped = 0f;

            if (justJumped)
            {
                modelNode.AnimationController.ResumeClip(jumpStandingClip, looping: false, time: 0f);
                modelNode.AnimationController.ResumeClip(jumpCrouchingClip, looping: false, time: 0f);
            }
            else
            {
                // Jump clips are non-looping on takeoff; when they finish while still airborne they freeze
                // on the tucked landing frame and collapse the mesh into a ball.
                var jumpStandingPausedAtEnd = modelNode.AnimationController.IsClipPausedAtEnd(jumpStandingClip);
                var jumpCrouchingPausedAtEnd = modelNode.AnimationController.IsClipPausedAtEnd(jumpCrouchingClip);

                if (jumpStandingPausedAtEnd)
                {
                    var holdTime = CsDemoPlayerWeaponAnimation.GetAirborneJumpSustainTime(modelNode, jumpStandingClip);
                    modelNode.AnimationController.ResumeClip(jumpStandingClip, looping: false, time: holdTime);
                }
                else
                {
                    modelNode.AnimationController.SetAnimationProperties(jumpStandingClip, null, looping: false);
                }

                if (jumpCrouchingPausedAtEnd)
                {
                    var holdTime = CsDemoPlayerWeaponAnimation.GetAirborneJumpSustainTime(modelNode, jumpCrouchingClip);
                    modelNode.AnimationController.ResumeClip(jumpCrouchingClip, looping: false, time: holdTime);
                }
                else
                {
                    modelNode.AnimationController.SetAnimationProperties(jumpCrouchingClip, null, looping: false);
                }

                // #region agent log
                if (jumpStandingPausedAtEnd || jumpCrouchingPausedAtEnd)
                {
                    AgentDebugLog.Write(
                        "H6",
                        "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:ApplyThirdPersonMovementJumpSustain",
                        "resumed paused jump clip while airborne",
                        new
                        {
                            runId = "post-fix",
                            jumpStandingClip,
                            jumpCrouchingClip,
                            jumpStandingPausedAtEnd,
                            jumpCrouchingPausedAtEnd,
                            weapon = weaponGroup.Item,
                        });
                }
                // #endregion
            }
        }

        var localDirection = movementInput.LengthSquared() > 1f
            ? Vector2.Normalize(movementInput)
            : movementInput.LengthSquared() > 1e-6f
                ? movementInput
                : GetLocalMovementDirection(velocity2D, yawDegrees);

        var headingWeights = new Dictionary<Heading, float>();
        var headingTotal = 0f;
        foreach (var (heading, headingVector) in HeadingVectors)
        {
            var weight = MathF.Max(0f, Vector2.Dot(localDirection, headingVector));
            headingWeights[heading] = weight;
            headingTotal += weight;
        }

        if (headingTotal > 0f)
        {
            foreach (var heading in headingWeights.Keys.ToList())
            {
                headingWeights[heading] /= headingTotal;
            }
        }

        foreach (var posture in Enum.GetValues<Posture>())
        {
            var postureWeight = posture == Posture.Standing ? standing : crouchBlend;

            modelNode.AnimationController.SetAnimationWeight(
                GetThirdpersonAnim(weaponGroup, posture, MovementState.Stopped),
                stopped * postureWeight);
            modelNode.AnimationController.SetAnimationWeight(
                GetThirdpersonAnim(weaponGroup, posture, MovementState.Jumping),
                jumping * postureWeight);
        }

        foreach (var heading in HeadingVectors.Keys)
        {
            var headingWeight = headingWeights.TryGetValue(heading, out var weight) ? weight : 0f;

            foreach (var (posture, movement) in new[] {
                (Posture.Crouching, MovementState.Walking),
                (Posture.Standing, MovementState.Walking),
                (Posture.Standing, MovementState.Running),
            })
            {
                var postureWeight = posture == Posture.Standing ? standing : crouchBlend;
                var movementWeight = movement switch
                {
                    MovementState.Walking => walking,
                    MovementState.Running => running,
                    _ => 0f,
                };

                modelNode.AnimationController.SetAnimationWeight(
                    GetThirdpersonAnim(weaponGroup, posture, movement, heading),
                    headingWeight * movementWeight * postureWeight,
                    false);

                modelNode.AnimationController.SetAnimationProperties(
                    GetThirdpersonAnim(weaponGroup, posture, movement, heading),
                    null,
                    looping: true,
                    timeScale: movement == MovementState.Running
                        ? runTimeScale
                        : posture == Posture.Crouching ? crouchWalkTimeScale : walkTimeScale);
            }

            if (running + walking == 0f)
            {
                modelNode.AnimationController.SetAnimationProperties(
                    GetThirdpersonAnim(weaponGroup, Posture.Standing, MovementState.Walking, heading),
                    0f,
                    looping: true);
            }
        }

        modelNode.AnimationController.SetAnimationWeight(BreathingClip, 1f);

        EnsureMixerActiveClip(modelNode);

        var stateSignature = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{weaponGroup.Item}:{speed2D / 25f:0}:{onGround}:{justJumped}:{crouchBlend:0.0}:{isWalking}:{stopped:0.00}:{walking:0.00}:{running:0.00}:{jumping:0.00}:{modelNode.AnimationController.ActiveAnimation?.Name}:{modelNode.AnimationController.Frame}");
        var debugState = MovementDebugStates.GetOrCreateValue(modelNode);
        if (debugState.LastSignature != stateSignature)
        {
            debugState.LastSignature = stateSignature;
            // #region agent log
            AgentDebugLog.Write(
                "H2,H3,H5",
                "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:ApplyThirdPersonMovementWeights",
                "movement weights applied to model",
                new
                {
                    weapon = weaponGroup.Item,
                    defaultWeapon = weaponGroup.DefaultItem,
                    speed = speed2D,
                    isPlaying,
                    isPaused = modelNode.AnimationController.IsPaused,
                    onGround,
                    justJumped,
                    crouchBlend,
                    isWalking,
                    stopped,
                    walking,
                    running,
                    jumping,
                    localDirectionX = localDirection.X,
                    localDirectionY = localDirection.Y,
                    activeAnimation = modelNode.AnimationController.ActiveAnimation?.Name,
                    activeClipFinished = modelNode.AnimationController.ActiveClipFinished,
                    frame = modelNode.AnimationController.Frame,
                    time = modelNode.AnimationController.Time,
                    idleClip,
                    hasIdleClip = modelNode.Animations.ContainsKey(idleClip),
                    hasBreathingClip = modelNode.Animations.ContainsKey(BreathingClip),
                    crouchWalkTimeScale,
                    walkTimeScale,
                    runTimeScale,
                });
            // #endregion
        }
    }

    private static float GetLocomotionTimeScale(float speed, float authoredSpeed)
        => Math.Clamp(speed / authoredSpeed, 0.25f, 2.5f);

    private static Vector2 GetLocalMovementDirection(Vector2 velocity2D, float yawDegrees)
    {
        if (velocity2D.LengthSquared() <= 1f)
        {
            return new Vector2(0, 1);
        }

        var yawRad = float.DegreesToRadians(yawDegrees);
        var worldDir = Vector2.Normalize(velocity2D);
        var localX = worldDir.X * MathF.Cos(yawRad) + worldDir.Y * MathF.Sin(yawRad);
        var localY = -worldDir.X * MathF.Sin(yawRad) + worldDir.Y * MathF.Cos(yawRad);

        // CS2 world locomotion clips use model +Y as forward; entity facing is model +X.
        var animSpace = new Vector2(localY, localX);
        return animSpace.LengthSquared() > 1e-6f ? Vector2.Normalize(animSpace) : new Vector2(0, 1);
    }

    public static CsDemoPlayerWeaponAnimationGroup ResolveWeaponGroupForActions(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup weaponGroup)
        => ResolveAvailableWeaponGroup(modelNode, weaponGroup);

    public static bool UsesWeaponAnimFallback(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup weaponGroup)
    {
        var resolved = ResolveAvailableWeaponGroup(modelNode, weaponGroup);
        return !string.Equals(resolved.Item, weaponGroup.Item, StringComparison.Ordinal)
            || !string.Equals(resolved.DefaultItem, weaponGroup.DefaultItem, StringComparison.Ordinal);
    }

    private static CsDemoPlayerWeaponAnimationGroup ResolveAvailableWeaponGroup(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup weaponGroup)
    {
        EnsureThirdPersonAnimationsRegistered(modelNode, weaponGroup);

        var requestedIdleClip = GetThirdpersonAnim(weaponGroup, Posture.Standing, MovementState.Stopped);
        var requestedIdleExists = modelNode.Animations.ContainsKey(requestedIdleClip);
        var loggedResolutions = LoggedWeaponGroupResolutions.GetOrCreateValue(modelNode);
        if (requestedIdleExists)
        {
            if (loggedResolutions.Add($"{weaponGroup.Item}:{weaponGroup.DefaultItem}:ok"))
            {
                // #region agent log
                AgentDebugLog.Write(
                    "H1",
                    "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:ResolveAvailableWeaponGroup",
                    "weapon animation group resolved without fallback",
                    new
                    {
                        requestedWeapon = weaponGroup.Item,
                        defaultWeapon = weaponGroup.DefaultItem,
                        requestedIdleClip,
                        requestedIdleExists,
                        fallbackUsed = false,
                        animationCount = modelNode.Animations.Count,
                    });
                // #endregion
            }

            return weaponGroup;
        }

        EnsureThirdPersonAnimationsRegistered(modelNode, RifleWeaponGroup);
        if (loggedResolutions.Add($"{weaponGroup.Item}:{weaponGroup.DefaultItem}:fallback"))
        {
            // #region agent log
            AgentDebugLog.Write(
                "H1",
                "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:ResolveAvailableWeaponGroupFallback",
                "weapon animation group fell back to rifle",
                new
                {
                    requestedWeapon = weaponGroup.Item,
                    defaultWeapon = weaponGroup.DefaultItem,
                    requestedIdleClip,
                    requestedIdleExists,
                    fallbackWeapon = RifleWeaponGroup.Item,
                    rifleIdleExists = modelNode.Animations.ContainsKey(GetThirdpersonAnim(RifleWeaponGroup, Posture.Standing, MovementState.Stopped)),
                    animationCount = modelNode.Animations.Count,
                });
            // #endregion
        }

        return RifleWeaponGroup;
    }

    private static void EnsureThirdPersonAnimationsRegistered(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup weaponGroup)
    {
        var registered = RegisteredWeaponGroups.GetOrCreateValue(modelNode);
        var key = $"{weaponGroup.Item}:{weaponGroup.DefaultItem}";
        if (!registered.Add(key))
        {
            return;
        }

        RegisterThirdPersonAnimations(modelNode, weaponGroup);
    }

    private static void RegisterThirdPersonAnimations(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup weaponGroup)
    {
        var idleClip = GetThirdpersonAnim(weaponGroup, Posture.Standing, MovementState.Stopped);
        if (!TryEnsureClipLoaded(modelNode, idleClip))
        {
            // #region agent log
            AgentDebugLog.Write(
                "H1",
                "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:RegisterThirdPersonAnimationsSkipped",
                "skipped registering clips for weapon group with no idle clip",
                new
                {
                    weapon = weaponGroup.Item,
                    defaultWeapon = weaponGroup.DefaultItem,
                    idleClip,
                });
            // #endregion
            return;
        }

        var animationCountBefore = modelNode.Animations.Count;
        var activeBefore = modelNode.AnimationController.ActiveAnimation?.Name;
        var foundClips = 0;

        foreach (var posture in Enum.GetValues<Posture>())
        {
            foreach (var movement in Enum.GetValues<MovementState>())
            {
                foreach (var heading in Enum.GetValues<Heading>())
                {
                    var clip = GetThirdpersonAnim(weaponGroup, posture, movement, heading);
                    if (!TryEnsureClipLoaded(modelNode, clip))
                    {
                        continue;
                    }

                    foundClips++;
                    modelNode.SetAnimationByName(clip, -1);
                    modelNode.AnimationController.SetAnimationProperties(clip, 0f, looping: true);
                    modelNode.AnimationController.SetAnimationWeight(clip, 0f);
                }
            }
        }

        if (TryEnsureClipLoaded(modelNode, BreathingClip))
        {
            modelNode.SetAnimationByName(BreathingClip, -1);
        }

        EnsureMixerActiveClip(modelNode);
        modelNode.AnimationController.RegisterBoneMask("Breathing", new()
        {
            {"wpnPivot", 0f},
            {"wpnAimIntent", 0f},
            {"attachWorld", 0f},
            {"leg_upper_R", 0f},
            {"leg_upper_L", 0f},
            {"spine_0", 1f},
        }, "animation/skeletons/characters/worldmodel.vnmskel");
        EnsureWeaponActionBoneMaskRegistered(modelNode);
        modelNode.AnimationController.SetAnimationProperties(BreathingClip, 0f, looping: true, boneMask: "Breathing");
        modelNode.AnimationController.SetAnimationWeight(BreathingClip, 1f);

        // #region agent log
        AgentDebugLog.Write(
            "H1,H5",
            "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:RegisterThirdPersonAnimations",
            "registered third-person animation clips",
            new
            {
                weapon = weaponGroup.Item,
                defaultWeapon = weaponGroup.DefaultItem,
                animationCountBefore,
                animationCountAfter = modelNode.Animations.Count,
                foundClips,
                expectedClipSlots = Enum.GetValues<Posture>().Length * Enum.GetValues<MovementState>().Length * Enum.GetValues<Heading>().Length,
                idleClip = GetThirdpersonAnim(weaponGroup, Posture.Standing, MovementState.Stopped),
                hasIdleClip = modelNode.Animations.ContainsKey(GetThirdpersonAnim(weaponGroup, Posture.Standing, MovementState.Stopped)),
                hasJumpClip = modelNode.Animations.ContainsKey(GetThirdpersonAnim(weaponGroup, Posture.Standing, MovementState.Jumping)),
                hasBreathingClip = modelNode.Animations.ContainsKey(BreathingClip),
                activeBefore,
                activeAfter = modelNode.AnimationController.ActiveAnimation?.Name,
            });
        // #endregion
    }

    internal static bool TryEnsureClipLoaded(ModelSceneNode modelNode, string clip)
    {
        if (modelNode.Animations.ContainsKey(clip))
        {
            return true;
        }

        return modelNode.LoadAnimationClip(clip);
    }

    internal static void EnsureMixerActiveClip(ModelSceneNode modelNode)
    {
        if (modelNode.AnimationController.ActiveAnimation != null)
        {
            return;
        }

        if (!TryEnsureClipLoaded(modelNode, BreathingClip))
        {
            return;
        }

        modelNode.SetAnimationByName(BreathingClip, -1);
        modelNode.AnimationController.SetAnimationProperties(BreathingClip, 0f, looping: true, boneMask: "Breathing");
        modelNode.AnimationController.SetAnimationWeight(BreathingClip, 1f);

        // #region agent log
        AgentDebugLog.Write(
            "H8",
            "GUI/Types/GLViewers/CsDemoPlayerModelSetup.cs:EnsureMixerActiveClip",
            "restored mixer active clip after null activeAnimation",
            new
            {
                runId = "post-fix",
                clip = BreathingClip,
            });
        // #endregion
    }

    internal static void EnsureWeaponActionBoneMaskRegistered(ModelSceneNode modelNode)
    {
        var registered = WeaponActionMaskRegistered.GetOrCreateValue(modelNode);
        if (!registered.Add(WeaponActionBoneMask))
        {
            return;
        }

        var boneWeights = new Dictionary<string, float>();
        foreach (var bone in modelNode.AnimationController.Skeleton.Bones)
        {
            boneWeights[bone.Name] = IsLowerBodyBone(bone.Name) ? 0f : 1f;
        }

        modelNode.AnimationController.RegisterBoneMask(WeaponActionBoneMask, boneWeights);
    }

    private static bool IsLowerBodyBone(string boneName)
        => boneName.StartsWith("leg_", StringComparison.OrdinalIgnoreCase)
            || boneName.StartsWith("ankle_", StringComparison.OrdinalIgnoreCase)
            || boneName.StartsWith("ball_", StringComparison.OrdinalIgnoreCase)
            || boneName.StartsWith("toe_", StringComparison.OrdinalIgnoreCase)
            || boneName.StartsWith("foot_", StringComparison.OrdinalIgnoreCase);

    private static string GetThirdpersonAnim(
        CsDemoPlayerWeaponAnimationGroup weaponGroup,
        Posture posture,
        MovementState movement,
        Heading heading = Heading.West)
    {
        var item = weaponGroup.Item;
        var path = $"animation/anims/world/{item}/_default_{weaponGroup.DefaultItem}/";

        if (movement == MovementState.Stopped)
        {
            return posture == Posture.Standing
                ? $"{path}idle_{item}.vnmclip"
                : $"{path}idle_crouch_{item}.vnmclip";
        }

        if (movement == MovementState.Jumping)
        {
            return posture == Posture.Standing
                ? $"{path}jump_stand_{item}.vnmclip"
                : $"{path}jump_crouch_stand_{item}.vnmclip";
        }

        var movementType = posture == Posture.Crouching
            ? "crouch"
            : movement == MovementState.Running ? "run" : "walk";

        var direction = heading switch
        {
            Heading.North => "n",
            Heading.South => "s",
            Heading.East => "e",
            Heading.West => "w",
            Heading.NorthEast => "ne",
            Heading.SouthEast => "se",
            Heading.SouthWest => "sw",
            Heading.NorthWest => "nw",
            _ => throw new ArgumentOutOfRangeException(nameof(heading), heading, null)
        };

        return $"{path}{movementType}_{direction}_{item}.vnmclip";
    }
}
