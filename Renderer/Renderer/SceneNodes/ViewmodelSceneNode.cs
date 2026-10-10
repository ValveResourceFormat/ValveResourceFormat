using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Gameplay;
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
    /// The kick weapon recoil puts into the view and the weapon.
    /// </summary>
    public AimPunchServices AimPunchServices { get; } = new();

    /// <summary>
    /// The player arms.
    /// </summary>
    public ModelSceneNode Arms => this;

    /// <summary>
    /// The player legs.
    /// </summary>
    public ModelSceneNode Legs { get; set; }

    private readonly ModelSceneNode thirdpersonBody;
    private readonly List<ModelSceneNode> thirdpersonItems = [];

    readonly List<ModelSceneNode?> Items = [];
    readonly List<RenderMaterial> legsMaterials = [];

    ModelSceneNode? SelectedItem => Items.ElementAtOrDefault(SelectedItemIndex - 1);

    private int PreviousSelectedIndex;

    /// <summary>Item index of the knife.</summary>
    private const int KnifeItemIndex = 3;

    /// <summary>Item index of the smoke grenade.</summary>
    private const int SmokeItemIndex = 4;

    /// <summary>Item index of the high explosive grenade, first up on slot 4.</summary>
    private const int ExplosiveItemIndex = 5;

    /// <summary>Item index of the molotov, last in slot 4's cycle.</summary>
    private const int FireItemIndex = 6;

    private bool IsGrenadeSelected => SelectedItemIndex is SmokeItemIndex or ExplosiveItemIndex or FireItemIndex;

    private bool IsKnifeSelected => SelectedItemIndex == KnifeItemIndex;

    /// <summary>
    /// Gets a value indicating whether to draw the walk mode crosshair: the viewmodel is up
    /// (walk mode, camera attached to the eyes rather than orbiting) and the equipped item wants one.
    /// </summary>
    public bool ShowCrosshair => active && LayerEnabled && !IsKnifeSelected;

    /// <summary>
    /// The selected item slot.
    /// </summary>
    public int SelectedItemIndex
    {
        get;
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
            recoilIndex = 0f;
            queuedFire = false;
            SetState(AnimationState.Draw);
        }
    } = KnifeItemIndex;

    readonly SkeletonSceneNode PrimarySkeletonDebug;
    ParticleSceneNode? muzzleFlashParticle;
    ParticleSceneNode? molotovHeldParticle;

    private bool FirstPersonMode { get; set; } = true;
    private Matrix4x4 TargetTransform = Matrix4x4.Identity;
    private Matrix4x4 PlayerTransform = Matrix4x4.Identity;
    private float attackCooldown;
    private bool queuedFire;
    private float alternateAttackCooldown;
    private Vector3 currentBob = Vector3.Zero;

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
    private const string KnifeHitWallSound = "Weapon_Knife.HitWall";
    private const string KnifeLightHitSound = "Weapon_Knife.Hit.Slice";
    private const string KnifeHeavyHitSound = "Weapon_Knife.Hit.Heavy";
    private const float KnifeLightRange = 48f;
    private const float KnifeHeavyRange = 32f;
    private const float KnifeRangePadding = 18f;

    // Both knife buttons share one timer, which a swing that connects pushes back further
    private const float KnifeHitDelay = 0.1f;

    // A missed line trace is retried with swept spheres shrinking from 14 to 2 units, each ending that much
    // short, keeping the smallest that still connects. We are currently missing sphere traces, so cubes stand in.
    private const float KnifeSweepMaxRadius = 14f;
    private const float KnifeSweepRadiusStep = 3f;

    private static readonly string[] AttackSounds = [
        RifleAttackSound,
        PistolAttackSound,
        KnifeHitWallSound,
        KnifeLightHitSound,
        KnifeHeavyHitSound,
        JumpThrowSound,
    ];

    private static void CacheSounds()
    {
        Sound.Player?.Bank.RemoveSoundEvent("BaseExplosionEffect.Sound"); // src1_3d

        foreach (var soundEvent in AttackSounds)
        {
            Sound.Cache(soundEvent);
        }

        foreach (var soundEvent in CS2Projectile.Sounds)
        {
            Sound.Cache(soundEvent);
        }
    }

    // Returns whether a knife swing connected
    private bool PlayAttackSound(UserInput input, bool heavyKnifeAttack)
    {
        switch (SelectedItemIndex)
        {
            case 1:
                Sound.Play(RifleAttackSound, volume: AttackSoundVolume);
                FireBullet(input);
                return false;

            case 2:
                Sound.Play(PistolAttackSound, volume: AttackSoundVolume);
                FireBullet(input);
                return false;

            case KnifeItemIndex:
                var camera = input.Camera;
                var range = (heavyKnifeAttack ? KnifeHeavyRange : KnifeLightRange) + KnifeRangePadding;

                if (TraceKnifeSwing(input, camera.Location, camera.Forward, range) is not { } hit)
                {
                    return false;
                }

                // this is played in-ear but i'd like to keep it positional
                Sound.Play(KnifeHitWallSound, hit.HitPosition - new Vector3(0, 0, 60), volume: AttackSoundVolume);
                Sound.Play(heavyKnifeAttack ? KnifeHeavyHitSound : KnifeLightHitSound);
                SpawnKnifeDecal(input, hit.ContactPoint);
                return true;

            default:
                return false;
        }
    }

    private static Rubikon.TraceResult? TraceKnifeSwing(UserInput input, Vector3 from, Vector3 forward, float range)
    {
        var to = from + forward * range;
        var trace = TraceWorldAndEntities(input, from, to);

        if (trace.Hit)
        {
            trace.ContactPoint = trace.HitPosition;
            return trace;
        }

        Rubikon.TraceResult? hit = null;

        for (var radius = KnifeSweepMaxRadius; radius > 0f; radius -= KnifeSweepRadiusStep)
        {
            var sweepTo = to - forward * radius;
            var halfExtents = new Vector3(radius);

            var sweep = input.PhysicsWorld?.TraceAABB(from, sweepTo, halfExtents, string.Empty, computeContactPoint: true)
                ?? new Rubikon.TraceResult();

            input.EntitySystem?.TraceAABB(from, sweepTo, halfExtents, detectStartSolid: false, ref sweep);

            if (!sweep.Hit)
            {
                break;
            }

            hit = sweep;
        }

        return hit;
    }

    // How far past the contact point the scuff trace reaches. An entity sweep reports the swept box's centre
    // rather than the touch, so this covers the largest box as well as a surface met at an angle.
    private const float KnifeDecalTraceOvershoot = KnifeSweepMaxRadius + 4f;

    private void SpawnKnifeDecal(UserInput input, Vector3 contactPoint)
    {
        var from = input.Camera.Location;
        var toContact = contactPoint - from;

        if (toContact.LengthSquared() < 1e-4f)
        {
            return;
        }

        // Swept hits carry no surface property, so find the struck surface with a ray toward the contact
        var direction = Vector3.Normalize(toContact);
        var surface = TraceWorldAndEntities(input, from, contactPoint + direction * KnifeDecalTraceOvershoot);

        if (!surface.Hit)
        {
            return;
        }

        entitySystem.TempEntities.DispatchEffect("KnifeSlash",
            new EffectData(Scene, surface.HitPosition, surface.HitNormal, input.Camera.Forward, surface.SurfacePropertyHash, surface.HitEntity));
    }

    private const float BulletRange = 8192f;

    // m_szTracerParticle from weapons.vdata
    private const string RifleTracer = "particles/weapons/cs_weapon_fx/weapon_tracers_assrifle.vpcf";
    private const string PistolTracer = "particles/weapons/cs_weapon_fx/weapon_tracers_pistol.vpcf";

    // Ignores the tracer frequency
    private string SelectedTracer => SelectedItemIndex == 1 ? RifleTracer : PistolTracer;

    private Vector3 tracerStart;

    private void FireBullet(UserInput input)
    {
        var camera = input.Camera;

        // Bullets leave along the aim plus the punch as it stood before this shot kicked it
        var punch = AimPunchServices.Sample(shotTime) * AimPunchServices.BulletScale;
        var (direction, _, _) = Camera.GetDirectionVectors(
            camera.Pitch + float.DegreesToRadians(punch.X),
            camera.Yaw + float.DegreesToRadians(punch.Y),
            0f);

        var end = camera.Location + direction * BulletRange;
        var trace = TraceWorldAndEntities(input, camera.Location, end);

        if (trace.Hit)
        {
            end = trace.HitPosition;

            entitySystem.TempEntities.DispatchEffect("Impact",
                new EffectData(Scene, trace.HitPosition, trace.HitNormal, direction, trace.SurfacePropertyHash, trace.HitEntity));
        }

        if (input.EntitySystem?.TraceWaterSurface(camera.Location, end, out var waterSurface) == true)
        {
            entitySystem.TempEntities.DispatchEffect("gunshotsplash",
                new EffectData(Scene, waterSurface, Vector3.UnitZ, direction, Scale: float.Lerp(8f, 12f, Random.Shared.NextSingle())));
        }

        SpawnTracer(SelectedTracer, end);
    }

    private void SpawnTracer(string particleName, Vector3 end)
    {
        var particle = entitySystem.TempEntities.DispatchParticleEffect(particleName, Scene, tracerStart, Vector3.Normalize(end - tracerStart));

        if (particle == null)
        {
            return;
        }

        particle.SetControlPoint(1, Matrix4x4.CreateTranslation(end));

        // Marks the tracer as fired by the viewer
        particle.GetControlPoint(3).Position = Vector3.UnitX;
    }

    // The viewmodel is drawn with a narrower field of view than the world, so a point on it sits elsewhere
    // in the world for the same place on screen
    private Vector3 ViewmodelToWorld(Vector3 position, Camera camera)
    {
        var context = Scene.RendererContext;
        var viewmodelFov = context.ViewmodelFieldOfView * context.FieldOfView / 90f;
        var scale = MathF.Tan(float.DegreesToRadians(context.FieldOfView) * 0.5f)
            / MathF.Tan(float.DegreesToRadians(viewmodelFov) * 0.5f);

        var offset = position - camera.Location;
        var forward = camera.Forward * Vector3.Dot(offset, camera.Forward);

        return camera.Location + forward + (offset - forward) * scale;
    }

    // Brush and prop entities carry their own colliders, which move with them, so the world alone misses doors
    internal static Rubikon.TraceResult TraceWorldAndEntities(UserInput input, Vector3 from, Vector3 to, string collisionName = Rubikon.DecalGeometry)
    {
        var trace = input.PhysicsWorld?.TraceRay(from, to, collisionName) ?? new Rubikon.TraceResult();
        input.EntitySystem?.TraceRay(from, to, collisionName, ref trace);

        return trace;
    }

    private void SetKnifeCooldown(float delay, bool connected)
    {
        attackCooldown = alternateAttackCooldown = delay + (connected ? KnifeHitDelay : 0f);
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

    private readonly List<CS2Projectile> projectiles = [];
    private CS2Projectile? lastThrown;

    /// <summary>What each kind of grenade is thrown as, and what its detonation spawns.</summary>
    private readonly Dictionary<CS2Projectile.GrenadeKind, (Model Model, ParticleSystem? Effect, ParticleSystem? FlightEffect)> grenadeResources = [];

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

                throwStrength = attack2 ? 0f : 1f;

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
            throwStrength = MathUtils.Approach(throwStrength, idealStrength, dt * ThrowStrengthTransition);

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
            SmokeItemIndex => CS2Projectile.GrenadeKind.Smoke,
            FireItemIndex => CS2Projectile.GrenadeKind.Fire,
            _ => CS2Projectile.GrenadeKind.Explosive,
        };

        var projectile = AcquireProjectile(kind);

        if (projectile == null)
        {
            return;
        }

        var (origin, velocity) = CalculateThrow(input, throwStrength);
        projectile.Launch(origin, velocity, entitySystem.Player);

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

        // The mover under the player carries the throw too; riding is positional, so the ride
        // velocity is not already inside input.Velocity
        var carried = jumpThrow
            ? new Vector3(groundVelocity.X, groundVelocity.Y, input.PlayerMovement.JumpImpulse)
            : input.Velocity + input.PlayerMovement.RideVelocity;

        origin.Z += float.Lerp(-UnderhandThrowLower, 0f, throwStrength);

        var reach = origin + forward * ThrowTraceDistance;

        if (input.PhysicsWorld is { } physics)
        {
            var trace = CS2Projectile.SweepHull(physics, entitySystem, origin, reach);

            if (trace is { Hit: true, IsValid: true })
            {
                reach = trace.HitPosition;
            }
        }

        origin = reach - forward * ThrowPullback;

        return (origin, forward * speed + carried * ThrownPlayerVelocityScale);
    }

    private CS2Projectile? AcquireProjectile(CS2Projectile.GrenadeKind kind)
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

        var created = new CS2Projectile(entitySystem, Scene, resources.Model, kind, resources.Effect, resources.FlightEffect);

        entitySystem.AddEntity(created);
        projectiles.Add(created);

        return created;
    }

    private (float fire, float altFire) GetWeaponFireDelays()
        => SelectedItemIndex switch
        {
            1 => (0.1f, 2f),  // m_flCycleTime, m4a1_silencer
            2 => (0.17f, 2f), // m_flCycleTime, usp_silencer
            KnifeItemIndex => (0.4f, 1f),
            _ => (0.1f, 2f),
        };

    private const float TickInterval = 1f / 64f;

    // Both silenced weapons are held with the silencer on, which is their second mode
    private const int SilencerOnMode = 1;

    // m_nRecoilSeed, m_bIsFullAuto and the m_flRecoil* keys from weapons.vdata
    private static readonly RecoilPattern RifleRecoil = new(38965, fullAuto: true, angle: [0f, 0f], angleVariance: [65f, 65f], magnitude: [25f, 21f], magnitudeVariance: [3f, 0f]);

    private static readonly RecoilPattern PistolRecoil = new(5426, fullAuto: false, angle: [0f, 0f], angleVariance: [0f, 0f], magnitude: [29f, 23f], magnitudeVariance: [0f, 0f]);

    private RecoilPattern? SelectedRecoil => SelectedItemIndex switch
    {
        1 => RifleRecoil,
        2 => PistolRecoil,
        _ => null,
    };

    // m_flRecoilIndex: how far into the pattern the spray is, which unwinds between shots
    private float recoilIndex;
    private float shotTime;
    private float lastShotTime = float.NegativeInfinity;

    private void Recoil(UserInput input, RecoilPattern pattern, float time)
    {
        var (angle, magnitude) = pattern[SilencerOnMode, (int)recoilIndex];

        // The view punch has been decaying since the shot
        var viewPunch = AimPunchServices.Kick(time, angle, magnitude);
        input.PlayerMovement.AddViewPunch(viewPunch * MathF.Exp(-input.PlayerMovement.ViewPunchDecay * (uptime - time)));

        recoilIndex++;
        lastShotTime = time;
    }

    // Once a tick past the cycle time since the last shot, the recoil index falls to a hundredth of itself
    // per second
    private void DecayRecoilIndex(float time, float dt)
    {
        var (cycleTime, _) = GetWeaponFireDelays();

        if (recoilIndex <= 0f || time <= lastShotTime + cycleTime + TickInterval)
        {
            return;
        }

        recoilIndex *= MathF.Pow(0.01f, dt);

        if (recoilIndex <= 0.1f)
        {
            recoilIndex = 0f;
        }
    }

    /// <summary>
    /// Gets the running speed the equipped item allows, in world units per second.
    /// These are <c>max_player_speed</c> from the CS weapon scripts: heavier guns slow the player down.
    /// </summary>
    public float WeaponMaxSpeed
        => SelectedItemIndex switch
        {
            1 => 225f, // m4a1_silencer
            2 => 240f, // usp_silencer
            KnifeItemIndex => 250f,
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
        bodyActionRestarted = true;
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
    private const string MuzzleFlashAttachment = "muzzle_flash2";
    private const string MolotovHeldEffect = "particles/weapons/cs_weapon_fx/weapon_molotov_held.vpcf";
    private const string MolotovFlameAttachment = "molotov_particle";

    /// <summary>The world the weapons this viewmodel holds spawn their projectiles into.</summary>
    private readonly EntitySystem entitySystem;

    internal ViewmodelSceneNode(Scene scene, EntitySystem entitySystem, Model model)
        : base(scene, model, isWorldPreview: true)
    {
        this.entitySystem = entitySystem;

        LoadItemAnimations();

        SetState(AnimationState.Idle);
        TargetTransform = Transform;

        var ag2Player = AnimationController.CurrentPlayer!;
        PrimarySkeletonDebug = new SkeletonSceneNode(Scene, ag2Player.Pose, ag2Player.Skeleton)
        {
            LayerName = WorldLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
        };

        Scene.Add(PrimarySkeletonDebug, true);

        Legs = new ModelSceneNode(Scene, model, isWorldPreview: true)
        {
            LayerName = WorldLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling | ObjectTypeFlags.NoShadows,
            Parent = this,
        };
        Scene.Add(Legs, true);

        thirdpersonBody = new ModelSceneNode(Scene, model, isWorldPreview: true)
        {
            LayerName = WorldLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
            RenderPasses = CustomRenderPasses.DepthOnly,
            Parent = this,
        };
        thirdpersonBody.BoneMerge(Legs);
        Scene.Add(thirdpersonBody, true);

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

        // The body plays the graph that drives third person models, the model's default one
        if (Legs.AnimationGraphReferences.Count > 0 && Legs.LoadAnimationGraph(Legs.AnimationGraphReferences[0].GraphPath) is { } graph)
        {
            Legs.SetAnimationGraph(graph);
            bodyAnimator = new PlayerBodyAnimator(graph);
        }
        else
        {
            Scene.RendererContext.Logger.LogWarning("The first person model has no animation graph to play, its body will not animate");
        }
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
            Flags = ObjectTypeFlags.DisableVisCulling | ObjectTypeFlags.NoShadows,
            RenderPasses = CustomRenderPasses.Default | CustomRenderPasses.Viewmodel,
        };
        Scene.Add(model, true);
        Items.Add(model);

        model.Parent = this;

        var shadowItem = new ModelSceneNode(Scene, item)
        {
            LayerName = WorldLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
            RenderPasses = CustomRenderPasses.DepthOnly,
            Parent = this,
        };
        Scene.Add(shadowItem, true);
        thirdpersonItems.Add(shadowItem);

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
    /// <param name="scene">The scene the viewmodel is drawn in.</param>
    /// <param name="entitySystem">The world its weapons spawn projectiles into.</param>
    /// <returns>The loaded viewmodel, or <see langword="null"/> when its resources are missing.</returns>
    public static ViewmodelSceneNode? TryLoadCs2Viewmodel(Scene scene, EntitySystem entitySystem)
    {
        var loader = scene.RendererContext.FileLoader;

        Span<string> resources = [
            "agents/models/tm_professional/tm_professional_varf3.vmdl",
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

        var viewmodel = new ViewmodelSceneNode(scene, entitySystem, models[0]);
        foreach (var item in models[2..])
        {
            viewmodel.AddItem(item);
        }

        var primary = viewmodel.Items[0]!;
        var stattrakModule = new ModelSceneNode(scene, models[1])
        {
            LayerName = ViewmodelLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling | ObjectTypeFlags.NoShadows,
            RenderPasses = CustomRenderPasses.Default | CustomRenderPasses.Viewmodel,
        };

        scene.Add(stattrakModule, true);
        primary.AttachNode(stattrakModule, "stattrak");

        Span<(CS2Projectile.GrenadeKind Kind, Model Model, string Effect, string? FlightEffect)> grenades = [
            (CS2Projectile.GrenadeKind.Smoke, models[5], "particles/explosions_fx/explosion_smokegrenade.vpcf", null),
            (CS2Projectile.GrenadeKind.Explosive, models[6], "particles/explosions_fx/explosion_hegrenade.vpcf", null),
            (CS2Projectile.GrenadeKind.Fire, models[7], "particles/inferno_fx/molotov_explosion.vpcf", "particles/weapons/cs_weapon_fx/weapon_molotov_thrown.vpcf"),
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
        viewmodel.SelectedItemIndex = KnifeItemIndex;

        CacheSounds();

        viewmodel.LayerName = ViewmodelLayerName;
        viewmodel.Flags |= ObjectTypeFlags.DisableVisCulling | ObjectTypeFlags.NoShadows;
        viewmodel.RenderPasses |= CustomRenderPasses.Viewmodel;

        var molotovHeldResource = loader.LoadFileCompiled(MolotovHeldEffect);
        if (molotovHeldResource?.DataBlock is ParticleSystem molotovHeldSystem)
        {
            viewmodel.molotovHeldParticle = new ParticleSceneNode(scene, molotovHeldSystem)
            {
                LayerName = ViewmodelLayerName,
                Flags = ObjectTypeFlags.DisableVisCulling,
            };

            viewmodel.molotovHeldParticle.Stop();
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
        active = input.WalkMode;

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

        // Ends the frame it runs out below zero, by how long ago that was
        attackCooldown = attackCooldown > 0f ? attackCooldown - dt : 0f;
        alternateAttackCooldown = MathF.Max(0f, alternateAttackCooldown - dt);

        if (!LayerEnabled)
        {
            Scene.ActivateLayer(WorldLayerName);
            Scene.ActivateLayer(ViewmodelLayerName);
        }

        UpdateBodyAnimator(input, uptime, dt);

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

        DecayRecoilIndex(uptime, dt);

        if (IsGrenadeSelected)
        {
            ProcessGrenadeInput(input, dt);
        }
        else
        {
            var (fireDelay, altFireDelay) = GetWeaponFireDelays();

            // A pistol click during the cycle time is held and fires as soon as the cycle is up
            if (SelectedItemIndex == 2 && input.Pressed(TrackedKeys.MouseLeft))
            {
                queuedFire = true;
            }

            var requestedFire = Deployed && (SelectedItemIndex == 2
                ? queuedFire
                : input.Holding(TrackedKeys.MouseLeft));

            if (requestedFire && attackCooldown <= 0f)
            {
                var late = SelectedRecoil != null && !input.Pressed(TrackedKeys.MouseLeft) ? attackCooldown : 0f;
                shotTime = uptime + late;

                queuedFire = false;
                SetState(AnimationState.Attack);
                var connected = PlayAttackSound(input, heavyKnifeAttack: false);
                attackCooldown = late + fireDelay;

                if (SelectedRecoil is { } recoil)
                {
                    Recoil(input, recoil, shotTime);
                }

                if (IsKnifeSelected)
                {
                    SetKnifeCooldown(fireDelay, connected);
                }
                else if (muzzleFlashParticle != null)
                {
                    muzzleFlashParticle.Restart();
                }
            }
            else if (input.Holding(TrackedKeys.MouseRight) && alternateAttackCooldown <= 0f && Deployed)
            {
                SetState(AnimationState.AlternateAttack);
                alternateAttackCooldown = altFireDelay;

                if (IsKnifeSelected)
                {
                    SetKnifeCooldown(altFireDelay, PlayAttackSound(input, heavyKnifeAttack: true));
                }
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
            SelectedItemIndex = KnifeItemIndex;
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

        UpdateTransforms(input, uptime);
    }

    private PlayerBodyAnimator? bodyAnimator;
    private bool bodyActionRestarted;

    // Tells the body's animation graph what is held and what it is being used for
    private void UpdateBodyAnimator(UserInput input, float uptime, float dt)
    {
        if (bodyAnimator == null)
        {
            return;
        }

        var (category, type) = SelectedItemIndex switch
        {
            1 => ("weapon_category_rifle", "weapon_m4a1_silencer"),
            2 => ("weapon_category_pistol", "weapon_usp_silencer"),
            SmokeItemIndex => ("weapon_category_grenade", "weapon_smokegrenade"),
            ExplosiveItemIndex => ("weapon_category_grenade", "weapon_hegrenade"),
            FireItemIndex => ("weapon_category_grenade", "weapon_molotov"),
            _ => ("weapon_category_knife", "weapon_knife_karambit"),
        };

        // The secondary fire of the guns here takes their silencer off
        var (action, attackType) = State switch
        {
            AnimationState.Draw => ("action_deploy", string.Empty),
            AnimationState.PullPin => ("action_attack", "attack_grenade_ready"),
            AnimationState.ThrowCharge => ("action_attack", "attack_grenade_charge"),
            AnimationState.Attack or AnimationState.AlternateAttack when IsGrenadeSelected => ("action_attack", "attack_grenade_throw"),
            AnimationState.Attack when IsKnifeSelected => ("action_attack", "attack_knife_lightmiss"),
            AnimationState.AlternateAttack when IsKnifeSelected => ("action_attack", "attack_knife_heavymiss"),
            AnimationState.Attack => ("action_attack", "attack_gun_primaryfire"),
            AnimationState.AlternateAttack => ("action_silencer_detach", string.Empty),
            _ => ("action_idle", string.Empty),
        };

        bodyAnimator.Update(input, new PlayerBodyAnimator.WeaponState
        {
            Category = category,
            Type = type,
            IsSilenced = SelectedItemIndex is 1 or 2,
            MaxSpeed = WeaponMaxSpeed,
            Action = action,
            AttackType = attackType,
            ThrowStrength = throwStrength,
            ActionRestarted = bodyActionRestarted,
            AimPunch = AimPunchServices.Sample(uptime),
        }, dt);

        bodyActionRestarted = false;
    }

    /// <summary>
    /// Recomputes <see cref="TargetTransform"/> and <see cref="PlayerTransform"/> from the camera,
    /// including view bob. The player transform carries yaw only; camera pitch stays out of it.
    /// </summary>
    private void UpdateTransforms(UserInput input, float uptime)
    {
        var camera = input.Camera;
        camera.RecalculateDirectionVectors();

        // The weapon follows the punched view and kicks further still
        var punch = input.PlayerMovement.ViewPunchDegrees + AimPunchServices.Sample(uptime) * AimPunchServices.ViewmodelScale;
        var (punchedForward, _, punchedRight) = Camera.GetDirectionVectors(
            camera.Pitch + float.DegreesToRadians(punch.X),
            camera.Yaw + float.DegreesToRadians(punch.Y),
            camera.Roll);

        var forward = Vector3.Normalize(punchedForward);

        // This is the +Y (left) axis rather than right, which is why the rows below come out cyclically
        // permuted; viewmodelOffsetRot is tuned against that frame, so leave it be. Taken from the camera
        // rather than as Cross(worldUp, forward), which is the same vector but collapses looking straight down.
        var right = -punchedRight;
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

        var bobAmplitude = MathUtils.RemapValClamped(speed, 150f, 300f, 0f, 0.1f);

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

        // The body faces where its feet are planted, which the view can turn away from
        var bodyYaw = bodyAnimator != null ? float.DegreesToRadians(bodyAnimator.BodyYaw) : camera.Yaw;
        var playerYawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bodyYaw);
        var playerRotation = Quaternion.Normalize(playerYawRotation);
        PlayerTransform = Matrix4x4.CreateFromQuaternion(playerRotation) * Matrix4x4.CreateTranslation(input.PlayerMovement.Position);
    }

    private void UpdateThirdpersonItems(Scene.UpdateContext context)
    {
        var controller = thirdpersonBody.AnimationController;
        var weaponBone = controller.Skeleton.GetBoneIndex("wpn");

        for (var i = 0; i < thirdpersonItems.Count; i++)
        {
            var isHeld = weaponBone != -1 && i + 1 == SelectedItemIndex && (grenadeInHand || !IsGrenadeSelected);
            var item = thirdpersonItems[i];

            if (isHeld)
            {
                var itemController = item.AnimationController;
                var rootBone = itemController.Skeleton.GetBoneIndex("weapon");
                var toRootBone = rootBone != -1 ? itemController.InverseBindPose[rootBone] : Matrix4x4.Identity;

                item.Transform = toRootBone * controller.Pose[weaponBone] * PlayerTransform;
            }
            else
            {
                item.Transform = Matrix4x4.CreateScale(0f);
            }

            item.UpdateHierarchy(context);
        }
    }

    /// <inheritdoc/>
    public override void Update(Scene.UpdateContext context)
    {
        Transform = TargetTransform;

        if (!FirstPersonMode)
        {
            Transform *= Matrix4x4.CreateScale(0);
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

            Legs.UpdateHierarchy(context);

            thirdpersonBody.Transform = PlayerTransform;
            thirdpersonBody.UpdateHierarchy(context);
            UpdateThirdpersonItems(context);
        }

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
            item.UpdateHierarchy(context);
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

                Matrix4x4.Decompose(item.GetAttachmentTransform(MuzzleFlashAttachment), out _, out var muzzleRotation, out var muzzlePosition);
                tracerStart = ViewmodelToWorld(muzzlePosition, context.Camera);

                // The effect's control point configuration drives control point 0 from the weapon's muzzle_flash attachment
                if (muzzleFlashParticle != null)
                {
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

            // Unturned forward minus the forward for the yaw turned through over the window.
            // Standing still leaves this at zero.
            var deltaYaw = MathF.IEEERemainder(yaw - Sample(currentTime - SwayInterp), MathF.Tau);
            var (yawSin, yawCos) = MathF.SinCos(deltaYaw);

            // Composed as forward*x + right*-y + up*z. Right is the negated left axis,
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
        /// ends when it falls outside the history.
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
