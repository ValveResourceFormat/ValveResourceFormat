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

            deployTimeLeft = DeployDuration;
            recoilIndex = 0f;
            queuedFire = false;
            SetState(AnimationState.Draw);
        }
    } = KnifeItemIndex;

    readonly SkeletonSceneNode PrimarySkeletonDebug;
    ParticleSceneNode? muzzleFlashParticle;

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

    // What the weapons do to physics props: a bullet punches the first prop on its way, a
    // connecting knife swing shoves what it hit. Impulses are in prop-mass units (roughly
    // kilograms) times units per second: a rifle round blasts light props away and gives a
    // 60 kg planter a solid 200 u/s shove.
    private const float RifleBulletImpulse = 12000f;
    private const float PistolBulletImpulse = 6000f;
    private const float KnifeImpulse = 25000f;

    // What each attack deals to whatever it hits: the rifle and pistol rounds' base damage, and
    // the knife's first light swing and its heavy stab
    private const float RifleBulletDamage = 36f;
    private const float PistolBulletDamage = 35f;
    private const float KnifeLightDamage = 40f;
    private const float KnifeHeavyDamage = 65f;

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
    ];

    private static void CacheSounds()
    {
        foreach (var soundEvent in AttackSounds)
        {
            Sound.Cache(soundEvent);
        }
    }

    // After the push, so a prop the hit breaks passes the push on to its pieces
    private void DealDamage(BaseEntity? target, float amount, DamageType type, Vector3 direction)
    {
        if (target is IDamageable damageable)
        {
            damageable.TakeDamage(new DamageInfo(amount, type, entitySystem.Player, direction));
        }
    }

    // Returns whether a knife swing connected
    private bool PlayAttackSound(UserInput input, bool heavyKnifeAttack)
    {
        var rigidBodies = entitySystem.PhysicsOrNull;

        switch (SelectedItemIndex)
        {
            case 1:
                Sound.Play(RifleAttackSound, volume: AttackSoundVolume);
                FireBullet(input);
                DealDamage(rigidBodies?.ApplyImpactImpulse(input.Camera.Location, input.Camera.Forward, BulletRange, RifleBulletImpulse),
                    RifleBulletDamage, DamageType.Bullet, input.Camera.Forward);
                return false;

            case 2:
                Sound.Play(PistolAttackSound, volume: AttackSoundVolume);
                FireBullet(input);
                DealDamage(rigidBodies?.ApplyImpactImpulse(input.Camera.Location, input.Camera.Forward, BulletRange, PistolBulletImpulse),
                    PistolBulletDamage, DamageType.Bullet, input.Camera.Forward);
                return false;

            case KnifeItemIndex:
                var camera = input.Camera;
                var range = (heavyKnifeAttack ? KnifeHeavyRange : KnifeLightRange) + KnifeRangePadding;

                // A swing can connect with a prop the world trace cannot see, and shoving it is a
                // hit of its own
                var struck = rigidBodies?.ApplyImpactImpulse(camera.Location, camera.Forward, range, KnifeImpulse);
                DealDamage(struck, heavyKnifeAttack ? KnifeHeavyDamage : KnifeLightDamage, DamageType.Club, camera.Forward);
                var hitProp = struck != null;

                if (TraceKnifeSwing(input, camera.Location, camera.Forward, range) is not { } hit)
                {
                    if (!hitProp)
                    {
                        return false;
                    }

                    Sound.Play(heavyKnifeAttack ? KnifeHeavyHitSound : KnifeLightHitSound);
                    return true;
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
        var punch = AimPunchServices.Punch * AimPunchServices.BulletScale;
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

    private float uptime;
    private float deployTimeLeft;
    private float frameTime;

    /// <summary>Whether the item in hand has finished coming up. Opens half a frame early, because a press
    /// is only seen once a frame and waiting for the deploy to be strictly over always runs late.</summary>
    private bool Deployed => deployTimeLeft <= frameTime * 0.5f;

    /// <summary>Which lookat clip is playing, where the item has more than one.</summary>
    private int lookAtVariant;

    /// <summary>Whether the item can be inspected. One already under way holds off a second until halfway.</summary>
    private bool CanInspect
    {
        get
        {
            if (State != AnimationState.LookAt)
            {
                return true;
            }

            var animation = AnimationController.ActiveAnimation;

            return animation is not { Duration: > 0f }
                || AnimationController.Time >= animation.Duration * 0.5f;
        }
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
    private float lastShotTime = float.NegativeInfinity;

    private void Recoil(UserInput input, RecoilPattern pattern)
    {
        var (angle, magnitude) = pattern[SilencerOnMode, (int)recoilIndex];

        input.PlayerMovement.AddViewPunch(AimPunchServices.Kick(angle, magnitude));

        recoilIndex++;
        lastShotTime = uptime;
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
        var looping = newState == AnimationState.Idle;

        var timeScale = 1f; // 0.3f;

        var fadeIn = newState is AnimationState.Draw or AnimationState.Attack or AnimationState.AlternateAttack
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

    /// <summary>The world the weapons this viewmodel holds spawn their effects into.</summary>
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

    record struct Anim(string Idle, string Draw, string LookAt, string Attack, string? AltAttack = null, string? Attack2 = null, string? AltAttack2 = null, string? LookAt2 = null);

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
    };

    private void LoadItemAnimations()
    {
        foreach (var (_, anim) in ItemAnimations)
        {
            string?[] clips = [
                anim.Idle, anim.Draw, anim.LookAt, anim.Attack, anim.AltAttack, anim.Attack2, anim.AltAttack2, anim.LookAt2,
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

    // The arms, the stattrak module, then the items in slot order; the item entries double as
    // the models dropped weapons spawn with
    private static readonly string[] ViewmodelResources = [
        "agents/models/tm_professional/tm_professional_varf3.vmdl",
        "weapons/models/shared/stattrak/stattrak_module.vmdl",
        "weapons/models/m4a1_silencer/weapon_rif_m4a1_silencer.vmdl",
        "weapons/models/usp_silencer/weapon_pist_usp_silencer.vmdl",
        "weapons/models/knife/knife_karambit/weapon_knife_karambit.vmdl",
    ];

    private static string? ItemModelPath(int itemIndex)
        => itemIndex >= 1 && itemIndex + 1 < ViewmodelResources.Length ? ViewmodelResources[itemIndex + 1] : null;

    // The weapon drop: the toss leaves from the torso, a couple of hands below the eyes, so it
    // reads as let go rather than thrown from the face - with a bit more forward push to reach
    // as far as an eye-level toss would. Plus the flat spin it leaves the hand with.
    private const float DropSpawnDistance = 24f;
    private const float DropSpawnBelowEyes = 10f;
    private const float DropTossSpeed = 300f;
    private const float DropSpinSpeed = 2f;

    /// <summary>
    /// Drops the held item as a physics prop, CS2's G: the weapon model spawns tossed ahead of
    /// the view in its dropped ground state, and the hands draw the item anew. Being an ordinary
    /// physics prop, the dropped weapon can be picked up with +USE, shot and shoved around.
    /// </summary>
    private void DropHeldItem(UserInput input)
    {
        if (ItemModelPath(SelectedItemIndex) is not { } modelPath)
        {
            return;
        }

        var entities = entitySystem;
        var camera = input.Camera;
        var origin = camera.Location - new Vector3(0f, 0f, DropSpawnBelowEyes) + camera.Forward * DropSpawnDistance;

        // A synthesized prop_physics_override, as if the map had authored one here; defaultanim
        // is the ground state the game's dropped weapons rest in
        var data = new EntityLump.Entity { ParentLump = new EntityLump { Resource = new Resource() } };
        data.Add("classname", "prop_physics_override");
        data.Add("model", modelPath);
        data.Add("origin", FormattableString.Invariant($"{origin.X} {origin.Y} {origin.Z}"));

        // A quarter turn counter-clockwise off the view, so the weapon leaves the hand side-on
        // to the player the way the game presents a dropped gun
        data.Add("angles", FormattableString.Invariant($"0 {float.RadiansToDegrees(camera.Yaw) + 90f} 0"));
        data.Add("defaultanim", "dropped");

        var dropped = new PropPhysics(entities, new EntitySpawnInfo(data, Matrix4x4.Identity, "Entities", Scene, EntityNameFixup.None));
        dropped.Spawn();
        entities.AddEntity(dropped);

        if (dropped.HasBody)
        {
            // Thrown as if shoved on the stock: the push at the rear end of a side-on weapon
            // torques it flat about up, so it spins like a bottle rather than tumbling end
            // over end
            var body = dropped.Body;
            body.LinearVelocity = camera.Forward * DropTossSpeed + input.Velocity;
            body.AngularVelocity = Vector3.UnitZ * DropSpinSpeed;
        }

        // The hands come back up with the same item, endless-armory style
        deployTimeLeft = DeployDuration;
        SetState(AnimationState.Draw);
    }

    /// <summary>
    /// Try to load the CS2 viewmodel, returning null if the necessary resources are not found.
    /// </summary>
    /// <param name="scene">The scene the viewmodel is drawn in.</param>
    /// <param name="entitySystem">The world its weapons spawn effects into.</param>
    /// <returns>The loaded viewmodel, or <see langword="null"/> when its resources are missing.</returns>
    public static ViewmodelSceneNode? TryLoadCs2Viewmodel(Scene scene, EntitySystem entitySystem)
    {
        var loader = scene.RendererContext.FileLoader;

        List<Model> models = [];
        foreach (var name in ViewmodelResources)
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

        viewmodel.SelectedItemIndex = 2;
        viewmodel.SelectedItemIndex = KnifeItemIndex;

        CacheSounds();

        viewmodel.LayerName = ViewmodelLayerName;
        viewmodel.Flags |= ObjectTypeFlags.DisableVisCulling | ObjectTypeFlags.NoShadows;
        viewmodel.RenderPasses |= CustomRenderPasses.Viewmodel;

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

        AimPunchServices.Update(dt);

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

        DecayRecoilIndex(uptime, dt);

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

            queuedFire = false;
            SetState(AnimationState.Attack);
            var connected = PlayAttackSound(input, heavyKnifeAttack: false);
            attackCooldown = late + fireDelay;

            if (SelectedRecoil is { } recoil)
            {
                Recoil(input, recoil);
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
        else if (input.Pressed(TrackedKeys.Q))
        {
            SelectPreviousItem();
        }
        else if (input.Pressed(TrackedKeys.G))
        {
            // Never gated on the deploy timer or any other action: dropping is always allowed
            DropHeldItem(input);
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
            _ => ("weapon_category_knife", "weapon_knife_karambit"),
        };

        // The secondary fire of the guns here takes their silencer off
        var (action, attackType) = State switch
        {
            AnimationState.Draw => ("action_deploy", string.Empty),
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
            ActionRestarted = bodyActionRestarted,
            AimPunch = AimPunchServices.Punch,
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
        var punch = input.PlayerMovement.ViewPunchDegrees + AimPunchServices.Punch * AimPunchServices.ViewmodelScale;
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
            var isHeld = weaponBone != -1 && i + 1 == SelectedItemIndex;
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

            if (AnimationController.ActiveClipFinished && State != AnimationState.Idle)
            {
                SetState(AnimationState.Idle);
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
            var isSelected = i == SelectedItemIndex;
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
