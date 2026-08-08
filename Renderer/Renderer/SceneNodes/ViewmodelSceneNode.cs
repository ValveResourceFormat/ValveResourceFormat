using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// First-person viewmodel scene node (player arms, weapon items and legs) driven by animgraph 2 clips.
/// </summary>
public class ViewmodelSceneNode : ModelSceneNode
{
    /// <summary>
    /// Viewmodel offset in viewmodel space (forward, right, up).
    /// </summary>
    public Vector3 ViewmodelOffset { get; set; } = new Vector3(5, -2, -2);

    /// <summary>
    /// Viewmodel sway, trailing the arms behind the view as it turns.
    /// </summary>
    public ViewmodelLag Lag { get; } = new();

    /// <summary>
    /// The player arms.
    /// </summary>
    public ModelSceneNode Arms => this;

    /// <summary>
    /// The player legs.
    /// </summary>
    public ModelSceneNode Legs { get; set; }

    readonly List<ModelSceneNode?> Items = [];
    readonly List<RenderMaterial> legsMaterials = [];

    ModelSceneNode? SelectedItem => Items.ElementAtOrDefault(SelectedItemIndex - 1);

    private int PreviousSelectedIndex;

    /// <summary>Item index of the smoke grenade.</summary>
    private const int SmokeItemIndex = 4;

    /// <summary>Item index of the high explosive grenade, first up on slot 4.</summary>
    private const int ExplosiveItemIndex = 5;

    /// <summary>Item index of the molotov, last in slot 4's cycle.</summary>
    private const int FireItemIndex = 6;

    private bool IsGrenadeSelected => SelectedItemIndex is SmokeItemIndex or ExplosiveItemIndex or FireItemIndex;

    /// <summary>
    /// The selected item slot.
    /// </summary>
    public int SelectedItemIndex
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            PreviousSelectedIndex = field;
            field = value;

            CancelGrenadeThrow();
            deployTimeLeft = DeployDuration;
            SetState(AnimationState.Draw);
        }
    } = 3;

    readonly SkeletonSceneNode PrimarySkeletonDebug;
    ParticleSceneNode? muzzleFlashParticle;
    ParticleSceneNode? molotovHeldParticle;

    private bool FirstPersonMode { get; set; } = true;
    private Matrix4x4 TargetTransform = Matrix4x4.Identity;
    private Matrix4x4 PlayerTransform = Matrix4x4.Identity;
    private float attackCooldown;
    private float alternateAttackCooldown;
    private Vector3 currentBob = Vector3.Zero;

    private Vector2 currentWalkDirection = new(0, 1);

    private bool restartInAirAnim;
    private float inAirExitTimer;
    private const float InAirExitFade = 0.1f;
    private float previousUptime;

    // Animations stay paused until the player leaves noclip, otherwise clips fire sound events while nothing is visible.
    private bool active;

    /// <summary>
    /// Selects the previously selected item (used for quick weapon switching).
    /// </summary>
    public void SelectPreviousItem()
    {
        SelectedItemIndex = PreviousSelectedIndex;
    }

    enum AnimationState
    {
        Idle,
        Draw,
        LookAt,
        Attack,
        AlternateAttack,
        PullPin,
        ThrowCharge,
    }

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
        InAir,
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

    private static readonly Posture[] Postures = Enum.GetValues<Posture>();

    /// <summary>Direction vector per heading, indexed by <see cref="Heading"/>.</summary>
    private static readonly Vector2[] HeadingVectors = BuildHeadingVectors();

    private static Vector2[] BuildHeadingVectors()
    {
        var vectors = new Vector2[Enum.GetValues<Heading>().Length];
        vectors[(int)Heading.North] = new(0, 1);
        vectors[(int)Heading.NorthEast] = Vector2.Normalize(new(1, 1));
        vectors[(int)Heading.East] = new(1, 0);
        vectors[(int)Heading.SouthEast] = Vector2.Normalize(new(1, -1));
        vectors[(int)Heading.South] = new(0, -1);
        vectors[(int)Heading.SouthWest] = Vector2.Normalize(new(-1, -1));
        vectors[(int)Heading.West] = new(-1, 0);
        vectors[(int)Heading.NorthWest] = Vector2.Normalize(new(-1, 1));
        return vectors;
    }

    /// <summary>Clip names precomputed for every state so the per-frame blend never builds strings.</summary>
    private static readonly string[,,] ThirdpersonAnims = BuildThirdpersonAnims();

    private static string[,,] BuildThirdpersonAnims()
    {
        var movements = Enum.GetValues<MovementState>();
        var headings = Enum.GetValues<Heading>();
        var anims = new string[Postures.Length, movements.Length, headings.Length];

        foreach (var posture in Postures)
        {
            foreach (var movement in movements)
            {
                foreach (var heading in headings)
                {
                    anims[(int)posture, (int)movement, (int)heading] = BuildThirdpersonAnim(posture, movement, heading);
                }
            }
        }

        return anims;
    }

    private static string GetThirdpersonAnim(Posture posture, MovementState movement, Heading heading = Heading.West)
        => ThirdpersonAnims[(int)posture, (int)movement, (int)heading];

    private static string BuildThirdpersonAnim(Posture posture, MovementState movement, Heading heading)
    {
        const string item = "rifle";
        const string path = $"animation/anims/world/{item}/_default_{item}/";

        if (movement == MovementState.Stopped)
        {
            return posture == Posture.Standing
                ? $"animation/anims/world/{item}/_default_{item}/idle_{item}.vnmclip"
                : $"animation/anims/world/{item}/_default_{item}/idle_crouch_{item}.vnmclip";
        }

        if (movement == MovementState.Jumping)
        {
            return posture == Posture.Standing
                ? $"animation/anims/world/{item}/_default_{item}/jump_stand_{item}.vnmclip"
                : $"animation/anims/world/{item}/_default_{item}/jump_crouch_stand_{item}.vnmclip";
        }

        if (movement == MovementState.InAir)
        {
            return posture == Posture.Standing
                ? $"animation/anims/world/{item}/_default_{item}/inair_stand_{item}.vnmclip"
                : $"animation/anims/world/{item}/_default_{item}/inair_crouch_stand_{item}.vnmclip";
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

        if (movement == MovementState.Stopped)
        {
            direction = "stopped";
        }

        var anim = $"{path}{movementType}_{direction}_{item}.vnmclip";
        return anim;
    }

    AnimationState State { get; set; } = AnimationState.Idle;

    /// <summary>
    /// Gets the currently selected animation path based on the active slot and state.
    /// </summary>
    public string TargetAnimation
    {
        get
        {
            if (ItemAnimations.TryGetValue(SelectedItemIndex, out var anim))
            {
                return ViewmodelAnimPath + State switch
                {
                    AnimationState.Idle => anim.Idle,
                    AnimationState.Draw => anim.Draw,
                    AnimationState.LookAt => lookAtVariant == 1 ? anim.LookAt2 ?? anim.LookAt : anim.LookAt,
                    AnimationState.Attack => anim.Attack,
                    AnimationState.AlternateAttack => anim.AltAttack,
                    AnimationState.PullPin => anim.PullPin,

                    AnimationState.ThrowCharge => ChargeState switch
                    {
                        2 => anim.ChargeHigh,
                        0 => anim.ChargeLow,
                        _ => anim.ChargeMid,
                    },

                    _ => string.Empty,
                };
            }

            return string.Empty;
        }
    }

    // Attack sounds. In the game these come from weapons.vdata (m_aShootSounds).
    private const string RifleAttackSound = "Weapon_M4A1.Silenced";      // weapon_m4a1_silencer
    private const string PistolAttackSound = "Weapon_USP.SilencedShot";  // weapon_usp_silencer
    private const float AttackSoundVolume = 0.5f;
    private const string KnifeSlashSound = "Weapon_Knife.Slash";
    private const string KnifeHeavySwishSound = "Weapon_Knife.Swish.Heavy";
    private const string KnifeHitWallSound = "Weapon_Knife.HitWall";
    private const float KnifeLightRange = 48f;
    private const float KnifeHeavyRange = 32f;

    // Retry a missed line trace with a swept "head hull", making the swipe radial
    private static readonly AABB KnifeSwingHull = AABB.FromCenteredSize(new Vector3(32f, 32f, 36f));

    private static readonly string[] AttackSounds = [
        RifleAttackSound,
        PistolAttackSound,
        KnifeSlashSound,
        KnifeHeavySwishSound,
        KnifeHitWallSound,
        JumpThrowSound,
    ];

    private static void CacheSounds()
    {
        Sound.Player?.Bank.RemoveSoundEvent("BaseExplosionEffect.Sound"); // src1_3d

        foreach (var soundEvent in AttackSounds)
        {
            Sound.Cache(soundEvent);
        }

        foreach (var soundEvent in CS2ProjectileSceneNode.Sounds)
        {
            Sound.Cache(soundEvent);
        }
    }

    private void PlayAttackSound(UserInput input, bool heavyKnifeAttack)
    {
        switch (SelectedItemIndex)
        {
            case 1:
                Sound.Play(RifleAttackSound, volume: AttackSoundVolume);
                break;

            case 2:
                Sound.Play(PistolAttackSound, volume: AttackSoundVolume);
                break;

            case 3:
                var camera = input.Camera;
                var range = heavyKnifeAttack ? KnifeHeavyRange : KnifeLightRange;
                var from = camera.Location;
                var to = from + camera.Forward * range;

                var trace = input.PhysicsWorld?.TraceRay(from, to);

                if (trace is not { Hit: true })
                {
                    trace = input.PhysicsWorld?.TraceAABB(from, to, KnifeSwingHull, string.Empty);
                }

                if (trace is { Hit: true } hit)
                {
                    // this is played in-ear but i'd like to keep it positional
                    Sound.Play(KnifeHitWallSound, hit.HitPosition - new Vector3(0, 0, 60), volume: AttackSoundVolume);
                }
                else
                {
                    Sound.Play(heavyKnifeAttack ? KnifeHeavySwishSound : KnifeSlashSound);
                }

                break;
        }
    }

    private const float GrenadeThrowVelocity = 750f;
    private const float GrenadeThrowDelay = 0.1f;
    private const float ThrowVelocityScale = 0.9f;
    private const float MinThrowVelocity = 15f;
    private const float MaxThrowVelocity = 750f;
    private const float UnderhandThrowDampening = 0.3f;
    private const float UnderhandThrowLower = 12f;
    private const float ThrowStrengthTransition = 1.3f;
    private const float ThrowPitchBias = 10f;
    private const float ThrowTraceDistance = 22f;
    private const float ThrowPullback = 6f;
    private const float JumpThrowWindow = 0.2f;
    private const float ThrownPlayerVelocityScale = 1.25f;
    private const int MaxProjectiles = 8;

    private const string JumpThrowSound = "BaseGrenade.JumpThrowM";

    private readonly List<CS2ProjectileSceneNode> projectiles = [];
    private CS2ProjectileSceneNode? lastThrown;

    /// <summary>What each kind of grenade is thrown as, and what its detonation spawns.</summary>
    private readonly Dictionary<CS2ProjectileSceneNode.GrenadeKind, (Model Model, ParticleSystem? Effect, ParticleSystem? FlightEffect)> grenadeResources = [];

    private float uptime;
    private float jumpUptime = float.NegativeInfinity;
    private Vector3 groundEyePosition;
    private Vector3 groundVelocity;
    private bool jumpThrow;

    private bool pinPulled;
    private bool grenadeInHand = true;
    private float throwStrength = 1f;
    private float throwTimer;
    private float deployTimeLeft;
    private float frameTime;

    /// <summary>Whether the item in hand has finished coming up. Opens half a frame early, because a press
    /// is only seen once a frame and waiting for the deploy to be strictly over always runs late.</summary>
    private bool Deployed => deployTimeLeft <= frameTime * 0.5f;

    private void CancelGrenadeThrow()
    {
        pinPulled = false;
        throwTimer = 0f;
        throwStrength = 1f;
        grenadeInHand = true;
    }

    /// <summary>Moves <paramref name="value"/> toward <paramref name="target"/> without overshooting.</summary>
    private static float Approach(float target, float value, float speed)
    {
        var delta = target - value;

        return delta > speed ? value + speed
            : delta < -speed ? value - speed
            : target;
    }

    /// <summary>Which of the three charge poses the current throw strength holds.</summary>
    private int ChargeState => throwStrength switch
    {
        > 0.75f => 2,
        < 0.25f => 0,
        _ => 1,
    };

    /// <summary>Which lookat clip is playing, where the item has more than one.</summary>
    private int lookAtVariant;

    /// <summary>Whether the item can be inspected. One already under way holds off a second until halfway.</summary>
    private bool CanInspect
    {
        get
        {
            if (pinPulled || throwTimer > 0f || !grenadeInHand)
            {
                return false;
            }

            if (State != AnimationState.LookAt)
            {
                return true;
            }

            var animation = AnimationController.ActiveAnimation;

            return animation is not { Duration: > 0f }
                || AnimationController.Time >= animation.Duration * 0.5f;
        }
    }

    private void ProcessGrenadeInput(UserInput input, float dt)
    {
        if (throwTimer > 0f)
        {
            throwTimer -= dt;

            if (throwTimer <= 0f)
            {
                throwTimer = 0f;
                ThrowGrenade(input);
            }

            return;
        }

        var attack = input.Holding(TrackedKeys.MouseLeft);
        var attack2 = input.Holding(TrackedKeys.MouseRight);

        if (!pinPulled)
        {
            // Nothing comes out until the grenade is all the way up.
            if (grenadeInHand && Deployed && (attack || attack2))
            {
                pinPulled = true;

                if (attack2)
                {
                    throwStrength = 0f;
                }

                SetState(AnimationState.PullPin);
            }

            return;
        }

        if (attack || attack2)
        {
            // Primary raises the strength, secondary lowers it, holding both sits between the two.
            var idealStrength = 0.5f;

            if (attack)
            {
                idealStrength += 0.5f;
            }

            if (attack2)
            {
                idealStrength -= 0.5f;
            }

            // Walks rather than snaps, so a tap only bends the throw as far as it was held.
            var previousCharge = ChargeState;
            throwStrength = Approach(idealStrength, throwStrength, dt * ThrowStrengthTransition);

            // Only re-enter on a pose change; the strength itself moves every frame.
            if (State == AnimationState.ThrowCharge && ChargeState != previousCharge)
            {
                SetState(AnimationState.ThrowCharge);
            }

            return;
        }

        pinPulled = false;
        throwTimer = GrenadeThrowDelay;
        jumpThrow = JumpedWithin(JumpThrowWindow);

        SetState(ChargeState == 0 ? AnimationState.AlternateAttack : AnimationState.Attack);
    }

    private bool JumpedWithin(float window) => uptime - jumpUptime <= window;

    private void ThrowGrenade(UserInput input)
    {
        grenadeInHand = false;

        jumpThrow = jumpThrow || JumpedWithin(JumpThrowWindow);

        if (jumpThrow)
        {
            Sound.Play(JumpThrowSound);
        }

        var kind = SelectedItemIndex switch
        {
            SmokeItemIndex => CS2ProjectileSceneNode.GrenadeKind.Smoke,
            FireItemIndex => CS2ProjectileSceneNode.GrenadeKind.Fire,
            _ => CS2ProjectileSceneNode.GrenadeKind.Explosive,
        };

        var projectile = AcquireProjectile(kind);

        if (projectile == null)
        {
            return;
        }

        var (origin, velocity) = CalculateThrow(input, throwStrength);
        projectile.Launch(origin, velocity);

        lastThrown = projectile;
    }

    /// <summary>World position of the last grenade thrown while it is still on its way.</summary>
    private Vector3? GrenadeInFlightPosition => lastThrown is { InFlight: true } grenade ? grenade.Position : null;

    internal UserInput.OrbitFollow GetOrbitFollow()
        => GrenadeThrowPending
            ? new UserInput.OrbitFollow(true, null)
            : new UserInput.OrbitFollow(GrenadeInFlightPosition.HasValue, GrenadeInFlightPosition);

    /// <summary>Whether a grenade is being wound up or is waiting out the throw delay.</summary>
    private bool GrenadeThrowPending => pinPulled || throwTimer > 0f;

    /// <summary>Where and how fast a thrown grenade leaves the hand.</summary>
    private (Vector3 Origin, Vector3 Velocity) CalculateThrow(UserInput input, float throwStrength)
    {
        var camera = input.Camera;

        var pitch = float.RadiansToDegrees(camera.Pitch);
        var throwPitch = pitch - ThrowPitchBias * (90f - MathF.Abs(pitch)) / 90f;

        var speed = Math.Clamp(GrenadeThrowVelocity * ThrowVelocityScale, MinThrowVelocity, MaxThrowVelocity);
        speed *= float.Lerp(UnderhandThrowDampening, 1f, throwStrength);

        var (pitchSin, pitchCos) = MathF.SinCos(float.DegreesToRadians(throwPitch));
        var (yawSin, yawCos) = MathF.SinCos(camera.Yaw);
        var forward = new Vector3(yawCos * pitchCos, yawSin * pitchCos, -pitchSin);

        var origin = jumpThrow ? groundEyePosition : input.PlayerMovement.EyePosition;
        var carried = jumpThrow
            ? new Vector3(groundVelocity.X, groundVelocity.Y, input.PlayerMovement.JumpImpulse)
            : input.Velocity;

        origin.Z += float.Lerp(-UnderhandThrowLower, 0f, throwStrength);

        var reach = origin + forward * ThrowTraceDistance;

        if (input.PhysicsWorld is { } physics)
        {
            var trace = CS2ProjectileSceneNode.SweepHull(physics, origin, reach);

            if (trace is { Hit: true, IsValid: true })
            {
                reach = trace.HitPosition;
            }
        }

        origin = reach - forward * ThrowPullback;

        return (origin, forward * speed + carried * ThrownPlayerVelocityScale);
    }

    private CS2ProjectileSceneNode? AcquireProjectile(CS2ProjectileSceneNode.GrenadeKind kind)
    {
        foreach (var projectile in projectiles)
        {
            if (projectile.Kind == kind && !projectile.Live)
            {
                return projectile;
            }
        }

        if (!grenadeResources.TryGetValue(kind, out var resources))
        {
            return null;
        }

        if (projectiles.Count >= MaxProjectiles)
        {
            return projectiles.Find(projectile => projectile.Kind == kind);
        }

        var node = new CS2ProjectileSceneNode(Scene, resources.Model, kind, resources.Effect, resources.FlightEffect)
        {
            Parent = this,
        };

        Scene.Add(node, true);
        projectiles.Add(node);

        return node;
    }

    private (float fire, float altFire) GetWeaponFireDelays()
        => SelectedItemIndex switch
        {
            1 => (0.1f, 2f),
            2 => (0.1f, 2f),
            3 => (0.3f, 1f),
            _ => (0.1f, 2f),
        };

    /// <summary>
    /// Gets the running speed the equipped item allows, in world units per second.
    /// These are <c>max_player_speed</c> from the CS weapon scripts: heavier guns slow the player down.
    /// </summary>
    public float WeaponMaxSpeed
        => SelectedItemIndex switch
        {
            1 => 225f, // m4a1_silencer
            2 => 240f, // usp_silencer
            3 => 250f, // knife
            SmokeItemIndex => 245f,     // weapon_smokegrenade
            ExplosiveItemIndex => 245f, // weapon_hegrenade
            FireItemIndex => 245f,      // weapon_molotov
            _ => 250f,
        };

    /// <summary>
    /// Gets how long after this item is drawn before it can be used, <c>m_flDeployDuration</c> in
    /// weapons.vdata. A second for everything here bar the rifle.
    /// </summary>
    public float DeployDuration
        => SelectedItemIndex switch
        {
            1 => 1.133333f, // m4a1_silencer
            _ => 1f,
        };

    void SetState(AnimationState newState)
    {
        State = newState;
        var looping = newState is AnimationState.Idle or AnimationState.ThrowCharge;

        var timeScale = 1f; // 0.3f;

        var fadeIn = newState is AnimationState.Draw or AnimationState.Attack or AnimationState.AlternateAttack or AnimationState.PullPin
            ? 0f
            : 0.35f;

        var warp = newState == AnimationState.LookAt;

        AnimationController.IsPaused = false;
        AnimationController.Looping = looping;
        AnimationController.FrametimeMultiplier = timeScale;
        SetAnimationByName(TargetAnimation, fadeIn, warp);

        SelectedItem?.AnimationController.IsPaused = false;
        SelectedItem?.AnimationController.Looping = looping;
        SelectedItem?.AnimationController.FrametimeMultiplier = timeScale;
        SelectedItem?.SetAnimationByName(TargetAnimation, fadeIn, warp);
    }

    internal const string WorldLayerName = "Internal - First Person Model";
    internal const string ViewmodelLayerName = "Internal - First Person Viewmodel";
    private const string ViewmodelAnimPath = "animation/anims/viewmodel/";
    private const string BreathingClip = "animation/anims/world/shared/breathing.vnmclip";
    private const string LandedClip = "animation/anims/world/shared/jump_additive_land.vnmclip";
    private const string MuzzleFlashAttachment = "muzzle_flash2";
    private const string MolotovHeldEffect = "particles/weapons/cs_weapon_fx/weapon_molotov_held.vpcf";
    private const string MolotovFlameAttachment = "molotov_particle";

    internal ViewmodelSceneNode(Scene scene, Model model)
        : base(scene, model, isWorldPreview: true)
    {
        LoadItemAnimations();

        AnimationController.EnableFirstPersonConstraints = true;

        SetState(AnimationState.Idle);
        TargetTransform = Transform;

        var ag2Player = AnimationController.CurrentPlayer!;
        PrimarySkeletonDebug = new SkeletonSceneNode(Scene, ag2Player.Pose, ag2Player.Skeleton)
        {
            LayerName = WorldLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
            Enabled = false,
        };

        Scene.Add(PrimarySkeletonDebug, true);

        Legs = new ModelSceneNode(Scene, model, isWorldPreview: true)
        {
            LayerName = WorldLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
            Parent = this,
        };
        Scene.Add(Legs, true);

        SetActiveMeshGroups([
            "first_or_third_person_@2_#&firstperson_default"
        ]);

        // Cache material references for efficient uniform updates (exclude arms/viewmodel materials)
        var armsMaterials = Arms.RenderableMeshes
            .SelectMany(m => m.DrawCalls)
            .Select(dc => dc.Material)
            .ToHashSet();

        legsMaterials.AddRange(
            Legs.RenderableMeshes
                .SelectMany(m => m.DrawCalls)
                .Select(dc => dc.Material)
                .Except(armsMaterials)
        );

        Legs.AnimationController.TwistConstraints = [];
        Legs.AnimationController.Looping = true;

        foreach (var posture in Enum.GetValues<Posture>())
        {
            foreach (var movement in Enum.GetValues<MovementState>())
            {
                foreach (var heading in Enum.GetValues<Heading>())
                {
                    var clip = GetThirdpersonAnim(posture, movement, heading);
                    Legs.LoadAnimationClip(clip);
                    Legs.SetAnimationByName(clip, -1);
                    Legs.AnimationController.SetAnimationProperties(clip, 0f, looping: movement is not MovementState.Jumping
                                                                                                and not MovementState.InAir
                    );

                    if (Legs.AnimationController.ActiveAnimation == null)
                    {
                        Scene.RendererContext.Logger.LogWarning("Wrong animation path: {Clip}", clip);
                    }
                }
            }
        }

        Legs.LoadAnimationClip(LandedClip);
        Legs.LoadAnimationClip(BreathingClip);
        Legs.SetAnimationByName(LandedClip, -1);
        Legs.SetAnimationByName(BreathingClip, -1);

        // todo: parse from nmskel?
        Legs.AnimationController.RegisterBoneMask("Breathing", new()
        {
            {"wpnPivot", 0f},
            {"wpnAimIntent", 0f},
            {"attachWorld", 0f},
            {"leg_upper_R", 0f},
            {"leg_upper_L", 0f},
            {"spine_0", 1f},
        }, "animation/skeletons/characters/worldmodel.vnmskel");

        Legs.AnimationController.SetAnimationProperties(LandedClip, 0f, looping: false);
        Legs.AnimationController.SetAnimationProperties(BreathingClip, 0f, looping: true, boneMask: "Breathing");
        Legs.AnimationController.SetAnimationWeight(BreathingClip, 1f);
    }

    record struct Anim(string Idle, string Draw, string LookAt, string Attack, string? AltAttack = null, string? Attack2 = null, string? AltAttack2 = null,
        string? PullPin = null, string? ChargeLow = null, string? ChargeMid = null, string? ChargeHigh = null, string? LookAt2 = null);

    readonly Dictionary<int, Anim> ItemAnimations = new()
    {
        [1] = new Anim(
            "rifle/_default_rifle/idle_rifle.vnmclip",
            "rifle/_default_rifle/draw_rifle.vnmclip",
            "rifle/_default_rifle/lookat01_rifle.vnmclip",
            "rifle/_default_rifle/shoot1_rifle.vnmclip",
            "rifle/_default_rifle/silencer_detach_rifle.vnmclip"
        ),
        [2] = new Anim(
            "pistol/_default_pistol/idle_pistol.vnmclip",
            "pistol/_default_pistol/draw_pistol.vnmclip",
            "pistol/_default_pistol/lookat01_pistol.vnmclip",
            "pistol/_default_pistol/shoot1_pistol.vnmclip",
            "pistol/_default_pistol/silencer_detach_pistol.vnmclip"
        ),
        [3] = new Anim(
            "knife/knife_karambit/idle1_karambit.vnmclip",
            "knife/knife_karambit/draw_karambit.vnmclip",
            "knife/knife_karambit/lookat01_karambit.vnmclip",
            "knife/knife_karambit/light_miss1_karambit.vnmclip",
            "knife/knife_karambit/heavy_miss1_karambit.vnmclip",
            "knife/knife_karambit/light_miss2_karambit.vnmclip"
        ),
        [SmokeItemIndex] = new Anim(
            "grenade/grenade_smokegrenade/idle_smoke.vnmclip",
            "grenade/grenade_smokegrenade/draw_smoke.vnmclip",
            "grenade/grenade_smokegrenade/lookat01_smoke.vnmclip",
            "grenade/grenade_smokegrenade/throw_overhand_smoke.vnmclip",
            "grenade/grenade_smokegrenade/throw_underhand_smoke.vnmclip",
            PullPin: "grenade/grenade_smokegrenade/pullpin_smoke.vnmclip",
            ChargeLow: "grenade/grenade_smokegrenade/throwcharge_low_smoke.vnmclip",
            ChargeMid: "grenade/grenade_smokegrenade/throwcharge_mid_smoke.vnmclip",
            ChargeHigh: "grenade/grenade_smokegrenade/throwcharge_high_smoke.vnmclip",
            LookAt2: "grenade/grenade_smokegrenade/lookat02_smoke.vnmclip"
        ),
        [ExplosiveItemIndex] = new Anim(
            "grenade/grenade_hegrenade/idle_hegrenade.vnmclip",
            "grenade/grenade_hegrenade/draw_hegrenade.vnmclip",
            "grenade/grenade_hegrenade/lookat01_hegrenade.vnmclip",
            "grenade/grenade_hegrenade/throw_overhand_hegrenade.vnmclip",
            "grenade/grenade_hegrenade/throw_underhand_hegrenade.vnmclip",
            PullPin: "grenade/grenade_hegrenade/pullpin_hegrenade.vnmclip",
            ChargeLow: "grenade/grenade_hegrenade/throwcharge_low_hegrenade.vnmclip",
            ChargeMid: "grenade/grenade_hegrenade/throwcharge_mid_hegrenade.vnmclip",
            ChargeHigh: "grenade/grenade_hegrenade/throwcharge_high_hegrenade.vnmclip",
            LookAt2: "grenade/grenade_hegrenade/lookat02_hegrenade.vnmclip"
        ),
        [FireItemIndex] = new Anim(
            "grenade/grenade_molotov/idle_molotov.vnmclip",
            "grenade/grenade_molotov/draw_molotov.vnmclip",
            "grenade/grenade_molotov/lookat01_molotov.vnmclip",
            "grenade/grenade_molotov/throw_overhand_molotov.vnmclip",
            "grenade/grenade_molotov/throw_underhand_molotov.vnmclip",
            PullPin: "grenade/grenade_molotov/pullpin_molotov.vnmclip",
            ChargeLow: "grenade/grenade_molotov/throwcharge_low_molotov.vnmclip",
            ChargeMid: "grenade/grenade_molotov/throwcharge_mid_molotov.vnmclip",
            ChargeHigh: "grenade/grenade_molotov/throwcharge_high_molotov.vnmclip",
            LookAt2: "grenade/grenade_molotov/lookat02_molotov.vnmclip"
        ),
    };

    private void LoadItemAnimations()
    {
        foreach (var (_, anim) in ItemAnimations)
        {
            string?[] clips = [
                anim.Idle, anim.Draw, anim.LookAt, anim.Attack, anim.AltAttack, anim.Attack2, anim.AltAttack2,
                anim.PullPin, anim.ChargeLow, anim.ChargeMid, anim.ChargeHigh, anim.LookAt2,
            ];

            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }

                if (!LoadAnimationClip(ViewmodelAnimPath + clip))
                {
                    Scene.RendererContext.Logger.LogWarning("Wrong animation path: {Clip}", ViewmodelAnimPath + clip);
                }
            }
        }
    }

    private void AddItem(Model item)
    {
        var model = new ModelSceneNode(Scene, item)
        {
            LayerName = ViewmodelLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
            RenderPasses = CustomRenderPasses.Default | CustomRenderPasses.Viewmodel,
        };
        Scene.Add(model, true);
        Items.Add(model);

        model.Parent = this;

        foreach (var anim in Animations.Values)
        {
            if (anim is ClipAnimation { Clip.SecondaryAnimations.Length: > 0 } clipAnimation)
            {
                model.LoadAnimationClip(clipAnimation.Clip.SecondaryAnimations[0]);
            }
        }
    }

    /// <summary>
    /// Try to load the CS2 viewmodel, returning null if the necessary resources are not found.
    /// </summary>
    /// <param name="scene"></param>
    /// <returns></returns>
    public static ViewmodelSceneNode? TryLoadCs2Viewmodel(Scene scene)
    {
        var loader = scene.RendererContext.FileLoader;

        Span<string> resources = [
            "agents/models/ctm_st6/ctm_st6_varianti.vmdl",
            "weapons/models/shared/stattrak/stattrak_module.vmdl",
            "weapons/models/m4a1_silencer/weapon_rif_m4a1_silencer.vmdl",
            "weapons/models/usp_silencer/weapon_pist_usp_silencer.vmdl",
            "weapons/models/knife/knife_karambit/weapon_knife_karambit.vmdl",
            "weapons/models/grenade/smokegrenade/weapon_smokegrenade.vmdl",
            "weapons/models/grenade/hegrenade/weapon_hegrenade.vmdl",
            "weapons/models/grenade/molotov/weapon_molotov.vmdl",
        ];

        List<Model> models = [];
        foreach (var name in resources)
        {
            var resource = loader.LoadFileCompiled(name);
            if (resource?.DataBlock is not Model model)
            {
                return null;
            }

            models.Add(model);
        }

        var viewmodel = new ViewmodelSceneNode(scene, models[0]);
        foreach (var item in models[2..])
        {
            viewmodel.AddItem(item);
        }

        var primary = viewmodel.Items[0]!;
        var stattrakModule = new ModelSceneNode(scene, models[1])
        {
            LayerName = ViewmodelLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
            RenderPasses = CustomRenderPasses.Default | CustomRenderPasses.Viewmodel,
        };

        scene.Add(stattrakModule, true);
        primary.AttachNode(stattrakModule, "stattrak");

        Span<(CS2ProjectileSceneNode.GrenadeKind Kind, Model Model, string Effect, string? FlightEffect)> grenades = [
            (CS2ProjectileSceneNode.GrenadeKind.Smoke, models[5], "particles/explosions_fx/explosion_smokegrenade.vpcf", null),
            (CS2ProjectileSceneNode.GrenadeKind.Explosive, models[6], "particles/explosions_fx/explosion_hegrenade.vpcf", null),
            (CS2ProjectileSceneNode.GrenadeKind.Fire, models[7], "particles/inferno_fx/molotov_explosion.vpcf", "particles/weapons/cs_weapon_fx/weapon_molotov_thrown.vpcf"),
        ];

        foreach (var (kind, model, effect, flightEffect) in grenades)
        {
            viewmodel.grenadeResources[kind] = (
                model,
                loader.LoadFileCompiled(effect)?.DataBlock as ParticleSystem,
                flightEffect == null ? null : loader.LoadFileCompiled(flightEffect)?.DataBlock as ParticleSystem);

            viewmodel.AcquireProjectile(kind);
        }

        viewmodel.SelectedItemIndex = 2;
        viewmodel.SelectedItemIndex = 3;

        CacheSounds();

        viewmodel.LayerName = ViewmodelLayerName;
        viewmodel.Flags |= ObjectTypeFlags.DisableVisCulling;
        viewmodel.RenderPasses |= CustomRenderPasses.Viewmodel;

        var molotovHeldResource = loader.LoadFileCompiled(MolotovHeldEffect);
        if (molotovHeldResource?.DataBlock is ParticleSystem molotovHeldSystem)
        {
            viewmodel.molotovHeldParticle = new ParticleSceneNode(scene, molotovHeldSystem)
            {
                LayerName = ViewmodelLayerName,
                Flags = ObjectTypeFlags.DisableVisCulling,
                LayerEnabled = false,
            };

            viewmodel.molotovHeldParticle.RenderPasses |= CustomRenderPasses.Viewmodel;

            scene.Add(viewmodel.molotovHeldParticle, true);
            viewmodel.Items[FireItemIndex - 1]!.AttachNode(viewmodel.molotovHeldParticle, MolotovFlameAttachment);
        }

        // Load muzzle flash particle
        var muzzleFlashResource = loader.LoadFileCompiled("particles/unified_weapon_fx/uweapon_muzflsh_riffle_fps.vpcf");
        if (muzzleFlashResource?.DataBlock is ParticleSystem particleSystem)
        {
            viewmodel.muzzleFlashParticle = new ParticleSceneNode(scene, particleSystem)
            {
                LayerName = ViewmodelLayerName,
                Flags = ObjectTypeFlags.DisableVisCulling,
                Parent = viewmodel,
            };

            // Added to, not assigned over: the node's passes are the ones its particle renderers draw in.
            viewmodel.muzzleFlashParticle.RenderPasses |= CustomRenderPasses.Viewmodel;

            scene.Add(viewmodel.muzzleFlashParticle, true);
        }

        scene.RendererContext.Logger.LogInformation($"Loaded first person model.");

        scene.Add(viewmodel, true);

        // don't render player model in noclip mode
        scene.DeactivateLayer(WorldLayerName);
        scene.DeactivateLayer(ViewmodelLayerName);

        return viewmodel;
    }

    /// <summary>
    /// Process input for the viewmodel, updating its transform to match the camera's orientation and position.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="uptime"></param>
    public void ProcessInput(UserInput input, float uptime)
    {
        active = !input.NoClip;

        var distanceFromFirstPersonEyes = Vector3.Distance(input.Camera.Location, input.PlayerMovement.EyePosition);

        var showViewmodelDistance = distanceFromFirstPersonEyes < 35f;
        var attachViewmodelDistance = distanceFromFirstPersonEyes < 5f;

        FirstPersonMode = showViewmodelDistance;

        if (!attachViewmodelDistance)
        {
            // The transform keeps tracking the camera while detached so particles anchored to the
            // muzzle never see a frozen control point
            UpdateTransforms(input, uptime);

            // don't render player model in noclip mode
            if (LayerEnabled)
            {
                Scene.DeactivateLayer(WorldLayerName);
                Scene.DeactivateLayer(ViewmodelLayerName);
            }

            return;
        }

        var dt = 0f;
        if (previousUptime > 0f)
        {
            dt = uptime - previousUptime;
            if (dt < 0f)
            {
                dt = 0f;
            }
        }
        previousUptime = uptime;

        if (inAirExitTimer > 0f)
        {
            inAirExitTimer = MathF.Max(0f, inAirExitTimer - dt);
        }

        if (!LayerEnabled)
        {
            Scene.ActivateLayer(WorldLayerName);
            Scene.ActivateLayer(ViewmodelLayerName);
        }

        UpdateTransforms(input, uptime);

        var camera = input.Camera;
        var speed = input.Velocity.Length();

        if (Legs?.AnimationController is { } legsController && legsController.CurrentPlayer is { } legsPlayer)
        {
            var crouched = input.PlayerMovement.CrouchBlend;
            var standing = 1f - crouched;

            Vector2 walkRun = new(float.Lerp(84f, 120f, standing), 250f);

            var running = MathUtils.Saturate((speed - walkRun.X) / (walkRun.Y - walkRun.X));
            var walking = MathUtils.Saturate(speed / walkRun.X) * (1f - running);
            var stopped = MathF.Max(0f, 1f - running - walking);

            var inAir = 0f;
            var jumping = 0f;
            var justJumped = false;

            if (!input.PlayerMovement.OnGround)
            {
                jumping = 1f;
                running = 0f;
                walking = 0f;
                stopped = 0f;

                justJumped = input.PlayerMovement.WasOnGroundLastFrame;
                restartInAirAnim = restartInAirAnim || justJumped;
                var restartedInAirAnim = false;

                foreach (var posture in Postures)
                {
                    var jumpingAnimName = GetThirdpersonAnim(posture, MovementState.Jumping);

                    if (justJumped)
                    {
                        legsController.SetAnimationProperties(jumpingAnimName, 0f, looping: false);
                    }
                    else
                    {
                        var inAirAnimName = GetThirdpersonAnim(posture, MovementState.InAir);

                        var jumpingActionFinished = legsPlayer.Clips.TryGetValue(jumpingAnimName, out var jumpClip) && jumpClip.IsPaused;
                        var inAirActionFinished = legsPlayer.Clips.TryGetValue(inAirAnimName, out var inAirClip) && inAirClip.IsPaused;

                        if (jumpingActionFinished)
                        {
                            jumping = 0f;
                            inAir = 1f;

                            if (inAirActionFinished && restartInAirAnim)
                            {
                                legsController.SetAnimationProperties(inAirAnimName, 0f, looping: false);
                                restartedInAirAnim = true;
                            }
                        }
                    }
                }

                if (restartedInAirAnim)
                {
                    restartInAirAnim = false;
                }
            }
            else
            {
                if (!input.PlayerMovement.WasOnGroundLastFrame)
                {
                    legsController.SetAnimationProperties(LandedClip, 0f, looping: false);
                    inAirExitTimer = InAirExitFade;
                }
            }

            // Compute a smoothed inAir weight: 1 while actually in-air, then fade to 0 over InAirExitFade when landing
            var inAirWeight = input.PlayerMovement.OnGround
                ? (inAirExitTimer > 0f ? inAirExitTimer / InAirExitFade : 0f)
                : inAir;

            // Calculate movement direction relative to the camera for directional blending.
            var desiredWalkDir = Vector2.Zero;
            var velocity2D = new Vector2(input.Velocity.X, input.Velocity.Y);
            if (velocity2D.LengthSquared() > 1e-4f)
            {
                var cameraForward2 = new Vector2(camera.Forward.X, camera.Forward.Y);
                var cameraRight2 = new Vector2(camera.Right.X, camera.Right.Y);

                if (cameraForward2.LengthSquared() > 1e-6f)
                {
                    cameraForward2 = Vector2.Normalize(cameraForward2);
                }

                if (cameraRight2.LengthSquared() > 1e-6f)
                {
                    cameraRight2 = Vector2.Normalize(cameraRight2);
                }

                var camRelative = new Vector2(
                    Vector2.Dot(velocity2D, cameraRight2),
                    Vector2.Dot(velocity2D, cameraForward2)
                );

                if (camRelative.LengthSquared() > 1e-6f)
                {
                    currentWalkDirection = Vector2.Normalize(camRelative);
                }
            }

            Span<float> headingWeights = stackalloc float[HeadingVectors.Length];
            var headingTotal = 0f;
            for (var i = 0; i < HeadingVectors.Length; i++)
            {
                var weight = MathF.Max(0f, Vector2.Dot(currentWalkDirection, HeadingVectors[i]));
                headingWeights[i] = weight;
                headingTotal += weight;
            }

            if (headingTotal > 0f)
            {
                for (var i = 0; i < headingWeights.Length; i++)
                {
                    headingWeights[i] /= headingTotal;
                }
            }

            foreach (var posture in Postures)
            {
                var t = posture == Posture.Standing ? standing : crouched;

                legsController.SetAnimationWeight(GetThirdpersonAnim(posture, MovementState.Stopped), stopped * t);
                legsController.SetAnimationWeight(GetThirdpersonAnim(posture, MovementState.Jumping), jumping * t);
                legsController.SetAnimationWeight(GetThirdpersonAnim(posture, MovementState.InAir), inAirWeight * t);
            }

            Span<(Posture, MovementState)> locomotionStates = [
                (Posture.Crouching, MovementState.Walking), // crouch
                (Posture.Standing, MovementState.Walking), // walk
                (Posture.Standing, MovementState.Running), // run
            ];

            // 8 way blend
            for (var headingIndex = 0; headingIndex < HeadingVectors.Length; headingIndex++)
            {
                var heading = (Heading)headingIndex;
                var headingWeight = headingWeights[headingIndex];

                foreach (var (posture, movement) in locomotionStates)
                {
                    var postureWeight = posture == Posture.Standing ? standing : crouched;
                    var movementWeight = movement switch
                    {
                        MovementState.Walking => walking,
                        MovementState.Running => running,
                        _ => 0f
                    };

                    legsController.SetAnimationWeight(GetThirdpersonAnim(posture, movement, heading), headingWeight * movementWeight * postureWeight, false);

                    // if we are stopped reset all times to zero.
                    if (running + walking == 0f)
                    {
                        legsController.SetAnimationProperties(GetThirdpersonAnim(Posture.Standing, MovementState.Walking, heading), 0f, looping: true);
                    }
                }
            }

            legsController.SetAnimationWeight(BreathingClip, 1f);
        }

        // Nothing is usable until it is all the way up, whichever item it is.
        if (deployTimeLeft > 0f)
        {
            deployTimeLeft = MathF.Max(0f, deployTimeLeft - dt);
        }

        frameTime = dt;
        this.uptime = uptime;

        if (input.PlayerMovement.OnGround)
        {
            groundEyePosition = input.PlayerMovement.EyePosition;
            groundVelocity = input.Velocity;
        }

        if (input.PlayerMovement.Jumped)
        {
            jumpUptime = uptime;
        }

        if (IsGrenadeSelected)
        {
            ProcessGrenadeInput(input, dt);
        }
        else
        {
            var (fireDelay, altFireDelay) = GetWeaponFireDelays();

            var requestedFire = Deployed && (SelectedItemIndex == 2
                ? input.Pressed(TrackedKeys.MouseLeft)
                : input.Holding(TrackedKeys.MouseLeft));

            if (requestedFire && attackCooldown <= 0f)
            {
                SetState(AnimationState.Attack);
                PlayAttackSound(input, heavyKnifeAttack: false);
                attackCooldown = fireDelay;
                if (SelectedItemIndex != 3 && muzzleFlashParticle != null)
                {
                    muzzleFlashParticle.Restart();
                }
            }
            else if (input.Holding(TrackedKeys.MouseRight) && alternateAttackCooldown <= 0f && Deployed)
            {
                SetState(AnimationState.AlternateAttack);

                if (SelectedItemIndex == 3)
                {
                    PlayAttackSound(input, heavyKnifeAttack: true);
                }

                alternateAttackCooldown = altFireDelay;
            }
        }

        if (input.Pressed(TrackedKeys.Slot1))
        {
            SelectedItemIndex = 1;
        }
        else if (input.Pressed(TrackedKeys.Slot2))
        {
            SelectedItemIndex = 2;
        }
        else if (input.Pressed(TrackedKeys.Slot3))
        {
            SelectedItemIndex = 3;
        }
        else if (input.Pressed(TrackedKeys.Slot4))
        {
            // Slot 4 holds the grenades: the HE comes up first, then the smoke, then the molotov.
            SelectedItemIndex = SelectedItemIndex switch
            {
                ExplosiveItemIndex => SmokeItemIndex,
                SmokeItemIndex => FireItemIndex,
                _ => ExplosiveItemIndex,
            };
        }
        else if (input.Pressed(TrackedKeys.Q))
        {
            SelectPreviousItem();
        }

        if (input.Pressed(TrackedKeys.F) && CanInspect)
        {
            // transition to a different lookat if possible
            if (ItemAnimations.TryGetValue(SelectedItemIndex, out var itemAnim) && itemAnim.LookAt2 != null)
            {
                lookAtVariant ^= 1;
            }

            SetState(AnimationState.LookAt);
        }
    }

    /// <summary>
    /// Recomputes <see cref="TargetTransform"/> and <see cref="PlayerTransform"/> from the camera,
    /// including view bob. The player transform carries yaw only; camera pitch stays out of it.
    /// </summary>
    private void UpdateTransforms(UserInput input, float uptime)
    {
        var camera = input.Camera;
        camera.RecalculateDirectionVectors();

        var forward = Vector3.Normalize(camera.Forward);
        var worldUp = Vector3.UnitZ;

        // This is the +Y (left) axis rather than right, which is why the rows below come out cyclically
        // permuted; viewmodelOffsetRot is tuned against that frame, so leave it be. Taken from the camera
        // rather than as Cross(worldUp, forward), which is the same vector but collapses looking straight down.
        var right = -camera.Right;
        var up = Vector3.Cross(forward, right);

        var cameraRotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            0, 0, 0, 1
        ));

        var viewmodelOffsetRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -float.DegreesToRadians(90))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -float.DegreesToRadians(90));
        var viewmodelRotation = Quaternion.Normalize(cameraRotation * viewmodelOffsetRot);

        var bobInputRotation = Quaternion.Inverse(viewmodelRotation);

        const float bobReferenceSpeed = 800f;
        const float bobOvershoot = 0.15f * bobReferenceSpeed; // max extra "speed" past the reference, added exponentially

        var speed = input.Velocity.Length();
        var bobSpeed = speed <= bobReferenceSpeed
            ? speed
            : bobReferenceSpeed + bobOvershoot * (1f - MathF.Exp(-(speed - bobReferenceSpeed) / bobOvershoot));

        // Scale the velocity direction to the clamped magnitude before deriving the bob, so
        // surf speeds do not throw the viewmodel off screen.
        var bobVelocity = speed > 1e-4f ? input.Velocity * (bobSpeed / speed) : Vector3.Zero;

        var targetBob = Vector3.Transform(bobVelocity * 0.005f, bobInputRotation);

        targetBob.Y = -targetBob.Y; // switch sideways movement to be leading instead of trailing
        targetBob.Z = MathF.Abs(targetBob.Z);
        targetBob.Y *= 0.3f;
        targetBob.Z *= 0.3f;

        currentBob = Vector3.Lerp(currentBob, targetBob, 0.5f);

        var bobAmplitude = MathUtils.Saturate((speed - 150f) / 150f) * 0.1f;

        if (!input.PlayerMovement.OnGround)
        {
            bobAmplitude = 0;
        }

        var bobFrequency = 18;
        var walkBob = new Vector3(1, 0.5f, 1) * MathF.Sin(uptime * bobFrequency) * bobAmplitude;

        // The gun trails the view by cl_wpn_sway_interp seconds as it turns
        var lag = Lag.Calculate(camera.Yaw, uptime);

        var rotationMatrix = Matrix4x4.CreateFromQuaternion(viewmodelRotation);
        var offset = Vector3.Transform(ViewmodelOffset - currentBob - walkBob + lag, viewmodelRotation);

        TargetTransform = rotationMatrix with { Translation = camera.Location + offset };

        var playerYawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, camera.Yaw);
        var playerRotation = Quaternion.Normalize(playerYawRotation);
        PlayerTransform = Matrix4x4.CreateFromQuaternion(playerRotation) * Matrix4x4.CreateTranslation(input.PlayerMovement.Position);
    }

    /// <inheritdoc/>
    public override void Update(Scene.UpdateContext context)
    {
        Transform = TargetTransform;

        if (!FirstPersonMode)
        {
            Transform *= Matrix4x4.CreateScale(0);
        }

        // Grenades already in the air keep flying whatever the viewmodel is doing, including
        // while the camera is off in noclip.
        foreach (var projectile in projectiles)
        {
            projectile.Simulate(context.Timestep);
            projectile.Update(context);
        }

        if (!active)
        {
            return;
        }

        if (Legs != null)
        {
            Legs.AnimationController.EnableFirstPersonLegs = FirstPersonMode;
            Legs.Transform = PlayerTransform;

            // Enable firstperson legs distortion shader effect
            var distortionValue = FirstPersonMode ? 1 : 0;
            foreach (var material in legsMaterials)
            {
                material.IntParams["g_bFirstpersonLegsDistortion"] = distortionValue;
            }

            Legs.Update(context);
        }

        attackCooldown = MathF.Max(0f, attackCooldown - context.Timestep);
        alternateAttackCooldown = MathF.Max(0f, alternateAttackCooldown - context.Timestep);

        var activeAnimation = AnimationController.ActiveAnimation;
        if (activeAnimation != null)
        {
            var frame = AnimationController.Frame;

            if (AnimationController.ActiveClipFinished)
            {
                if (State == AnimationState.PullPin)
                {
                    // Hold the grenade back until the throw button comes up.
                    SetState(AnimationState.ThrowCharge);
                }
                else if (State is AnimationState.Attack or AnimationState.AlternateAttack && !grenadeInHand)
                {
                    grenadeInHand = true;
                    deployTimeLeft = DeployDuration;
                    SetState(AnimationState.Draw);
                }
                else if (State is not AnimationState.Idle and not AnimationState.ThrowCharge)
                {
                    SetState(AnimationState.Idle);
                }
            }

            PrimarySkeletonDebug.Transform = Transform;
        }

        base.Update(context);

        // LocalBoundingBox = new AABB(Vector3.Zero, float.PositiveInfinity);

        static void UpdateItem(ModelSceneNode item, Scene.UpdateContext context, AABB bounds)
        {
            item.Update(context);
            item.LocalBoundingBox = bounds;
        }

        var i = 1;
        foreach (var item in Items)
        {
            var isSelected = i == SelectedItemIndex && (grenadeInHand || !IsGrenadeSelected);
            i++;

            if (item != null)
            {
                if (!isSelected)
                {
                    item.Transform = Matrix4x4.CreateScale(0);
                    UpdateItem(item, context, LocalBoundingBox);
                    continue;
                }

                var ag2Player = AnimationController.CurrentPlayer;

                if (ag2Player == null)
                {
                    continue;
                }

                var wpnIndex = ag2Player.Skeleton.GetBoneIndex("wpn");

                if (wpnIndex == -1)
                {
                    // context.TextRenderer.AddTextRelative("not found", 0.5f, 0.5f, 13, Color32.Blue, context.Camera);
                    continue;
                }

                var wpnTransform = ag2Player.Pose[wpnIndex];

                item.Transform = wpnTransform * Transform;
                UpdateItem(item, context, LocalBoundingBox);

                UpdateMolotovFlame();

                // The effect's control point configuration drives control point 0 from the weapon's muzzle_flash attachment
                if (muzzleFlashParticle != null)
                {
                    Matrix4x4.Decompose(item.GetAttachmentTransform(MuzzleFlashAttachment), out _, out var muzzleRotation, out var muzzlePosition);

                    muzzleFlashParticle.Transform = Matrix4x4.CreateFromQuaternion(muzzleRotation) * Matrix4x4.CreateTranslation(muzzlePosition);
                    muzzleFlashParticle.Update(context);
                }
            }
        }
    }

    private bool MolotovLit => SelectedItemIndex == FireItemIndex && grenadeInHand && (pinPulled || throwTimer > 0f);

    private void UpdateMolotovFlame()
    {
        if (molotovHeldParticle == null)
        {
            return;
        }

        if (!MolotovLit)
        {
            molotovHeldParticle.Stop();
        }
        else if (!molotovHeldParticle.IsPlaying)
        {
            molotovHeldParticle.Play();
        }
    }

    /// <summary>
    /// Viewmodel sway.
    /// </summary>
    public sealed class ViewmodelLag
    {
        /// <summary>How far back the viewmodel trails the view, in seconds (<c>cl_wpn_sway_interp</c>).</summary>
        public float SwayInterp { get; set; } = 0.1f;

        /// <summary>
        /// How far the trailing view angle pushes the viewmodel (<c>cl_wpn_sway_scale</c>).
        /// </summary>
        public float SwayScale { get; set; } = 0.32f;

        // Past view yaws, newest last. The window only needs one entry per frame, so this reaches
        // back well past the sway window even at very high framerates; older entries fall off.
        private readonly (float Time, float Yaw)[] history = new (float, float)[512];
        private int newest = -1;
        private int count;

        /// <summary>
        /// Records this frame's view yaw and returns the sway offset, in viewmodel space
        /// (forward, left, up).
        /// </summary>
        /// <param name="yaw">Current view yaw in radians.</param>
        /// <param name="currentTime">Seconds since startup.</param>
        public Vector3 Calculate(float yaw, float currentTime)
        {
            Record(currentTime, yaw);

            if (SwayInterp <= 0f)
            {
                return Vector3.Zero;
            }

            // AngleVectors of the yaw the view turned through over the window, measured against
            // an unturned forward vector. Standing still leaves this at zero.
            var deltaYaw = MathF.IEEERemainder(yaw - Sample(currentTime - SwayInterp), MathF.Tau);
            var (yawSin, yawCos) = MathF.SinCos(deltaYaw);

            // Source composes this as forward*x + right*-y + up*z. Right is the negated left axis,
            // so in a (forward, left, up) basis the components carry over unchanged.
            return new Vector3(1f - yawCos, -yawSin, 0f) * SwayScale;
        }

        private void Record(float time, float yaw)
        {
            newest = (newest + 1) % history.Length;
            history[newest] = (time, yaw);

            if (count < history.Length)
            {
                count++;
            }
        }

        /// <summary>
        /// Linearly interpolates the recorded yaw at <paramref name="time"/>, holding at the
        /// ends when it falls outside the history, as Source's CInterpolatedVar does.
        /// </summary>
        private float Sample(float time)
        {
            var newer = history[newest];

            for (var i = 1; i < count && time < newer.Time; i++)
            {
                var older = history[(newest - i + history.Length) % history.Length];

                if (older.Time <= time)
                {
                    var span = newer.Time - older.Time;
                    var t = span > 0f ? (time - older.Time) / span : 0f;

                    return MathUtils.LerpAngle(older.Yaw, newer.Yaw, t);
                }

                newer = older;
            }

            return newer.Yaw;
        }
    }
}
