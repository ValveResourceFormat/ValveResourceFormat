using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// Animgraph 2 model node.
/// </summary>
public class ViewmodelSceneNode : ModelSceneNode
{
    /// <summary>
    /// Viewmodel offset in viewmodel space (forward, right, up).
    /// </summary>
    public Vector3 ViewmodelOffset { get; set; } = new Vector3(0, -2, -2);

    /// <summary>
    /// The player arms.
    /// </summary>
    public ModelSceneNode Arms => this;

    /// <summary>
    /// The player legs.
    /// </summary>
    public ModelSceneNode Legs { get; set; }

    readonly List<ModelSceneNode?> Items = [];
    readonly List<Material> legsMaterials = [];

    ModelSceneNode? SelectedItem => Items.ElementAtOrDefault(SelectedItemIndex - 1);

    private int PreviousSelectedIndex;

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

            SetState(AnimationState.Draw);
        }
    } = 3;

    readonly SkeletonSceneNode PrimarySkeletonDebug;
    ParticleSceneNode? muzzleFlashParticle;

    private bool FirstPersonMode { get; set; } = true;
    private Matrix4x4 TargetTransform = Matrix4x4.Identity;
    private Matrix4x4 PlayerTransform = Matrix4x4.Identity;
    private float attackCooldown;
    private float alternateAttackCooldown;
    private Vector3 currentBob = Vector3.Zero;

    private Vector2 currentWalkDirection = new(0, 1);

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

    private static string GetThirdpersonAnim(Posture posture, MovementState movement, Heading heading = Heading.West)
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
                return "animation/anims/viewmodel/" + State switch
                {
                    AnimationState.Idle => anim.Idle,
                    AnimationState.Draw => anim.Draw,
                    AnimationState.LookAt => anim.LookAt,
                    AnimationState.Attack => anim.Attack,
                    AnimationState.AlternateAttack => anim.AltAttack,
                    _ => string.Empty,
                };
            }

            return string.Empty;
        }
    }

    private (float fire, float altFire) GetWeaponFireDelays()
        => SelectedItemIndex switch
        {
            1 => (0.1f, 2f),
            2 => (0.1f, 2f),
            3 => (0.3f, 1f),
            _ => (0.1f, 2f),
        };

    void SetState(AnimationState newState)
    {
        State = newState;
        var looping = newState == AnimationState.Idle;

        var timeScale = 1f; // 0.3f;

        var fadeIn = newState is AnimationState.Draw or AnimationState.Attack or AnimationState.AlternateAttack
            ? 0f
            : 0.35f;

        AnimationController.IsPaused = false;
        AnimationController.Looping = looping;
        AnimationController.FrametimeMultiplier = timeScale;
        SetAnimationByName(TargetAnimation, fadeIn);

        SelectedItem?.AnimationController.IsPaused = false;
        SelectedItem?.AnimationController.Looping = looping;
        SelectedItem?.AnimationController.FrametimeMultiplier = timeScale;
        SelectedItem?.SetAnimationByName(TargetAnimation, fadeIn);
    }

    internal const string WorldLayerName = "Internal - First Person Model";
    private const string BreathingClip = "animation/anims/world/shared/breathing.vnmclip";

    internal ViewmodelSceneNode(Scene scene, Model model)
        : base(scene, model, null, true)
    {
        AnimationController.EnableFirstPersonConstraints = true;
        SetState(AnimationState.Idle);
        TargetTransform = Transform;

        var ag2Controller = AnimationController.CurrentSubController!.Value.Handler;
        PrimarySkeletonDebug = new SkeletonSceneNode(Scene, ag2Controller, ag2Controller.Skeleton)
        {
            LayerName = WorldLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
            Enabled = false,
        };

        Scene.Add(PrimarySkeletonDebug, true);

        Legs = new ModelSceneNode(Scene, model)
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
            .Select(dc => dc.Material.Material)
            .ToHashSet();

        legsMaterials.AddRange(
            Legs.RenderableMeshes
                .SelectMany(m => m.DrawCalls)
                .Select(dc => dc.Material.Material)
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
                    Legs.SetAnimationByName(clip, -1);
                    Legs.AnimationController.SetAnimationProperties(clip, 0f, looping: true); // clips loop by default

                    if (Legs.AnimationController.ActiveAnimation == null)
                    {
                        Scene.RendererContext.Logger.LogWarning("Wrong animation path: {Clip}", clip);
                    }
                }
            }
        }

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

        Legs.AnimationController.SetAnimationProperties(BreathingClip, 0f, looping: true, boneMask: "Breathing");
        Legs.AnimationController.SetAnimationWeight(BreathingClip, 1f);
    }

    record struct Anim(string Idle, string Draw, string LookAt, string Attack, string? AltAttack = null, string? Attack2 = null, string? AltAttack2 = null);

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

    private void AddItem(Model item)
    {
        var model = new ModelSceneNode(Scene, item)
        {
            LayerName = WorldLayerName,
            Flags = ObjectTypeFlags.DisableVisCulling,
        };
        Scene.Add(model, true);
        Items.Add(model);

        model.Parent = this;

        foreach (var anim in Animations.Values)
        {
            if (anim.Clip is not null)
            {
                if (anim.Clip.SecondaryAnimations.Length > 0)
                {
                    model.LoadAnimationClip(anim.Clip.SecondaryAnimations[0]);
                }
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
            "weapons/models/m4a1_silencer/weapon_rif_m4a1_silencer.vmdl",
            "weapons/models/usp_silencer/weapon_pist_usp_silencer.vmdl",
            "weapons/models/knife/knife_karambit/weapon_knife_karambit.vmdl",
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
        foreach (var item in models[1..])
        {
            viewmodel.AddItem(item);
        }

        viewmodel.SelectedItemIndex = 2;
        viewmodel.SelectedItemIndex = 3;
        viewmodel.LayerName = WorldLayerName;
        viewmodel.Flags |= ObjectTypeFlags.DisableVisCulling;

        // Load muzzle flash particle
        var muzzleFlashResource = loader.LoadFileCompiled("particles/unified_weapon_fx/uweapon_muzflsh_riffle.vpcf"); // _fps
        if (muzzleFlashResource?.DataBlock is ParticleSystem particleSystem)
        {
            viewmodel.muzzleFlashParticle = new ParticleSceneNode(scene, particleSystem)
            {
                LayerName = WorldLayerName,
                Flags = ObjectTypeFlags.DisableVisCulling,
            };
            scene.Add(viewmodel.muzzleFlashParticle, true);
        }

        scene.RendererContext.Logger.LogInformation($"Loaded first person model.");

        scene.Add(viewmodel, true);

        // don't render player model in noclip mode
        scene.DeactivateLayer(WorldLayerName);

        return viewmodel;
    }

    /// <summary>
    /// Process input for the viewmodel, updating its transform to match the camera's orientation and position.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="uptime"></param>
    public void ProcessInput(UserInput input, float uptime)
    {
        var distanceFromFirstPersonEyes = Vector3.Distance(input.Camera.Location, input.PlayerMovement.EyePosition);

        var showViewmodelDistance = distanceFromFirstPersonEyes < 35f;
        var attachViewmodelDistance = distanceFromFirstPersonEyes < 5f;

        FirstPersonMode = showViewmodelDistance;

        if (!attachViewmodelDistance)
        {
            // don't render player model in noclip mode
            if (LayerEnabled)
            {
                Scene.DeactivateLayer(WorldLayerName);
            }

            return;
        }

        if (!LayerEnabled)
        {
            Scene.ActivateLayer(WorldLayerName);
        }

        var camera = input.Camera;
        camera.RecalculateDirectionVectors();

        // Build a stable camera orientation quaternion from direction vectors.
        var forward = Vector3.Normalize(camera.Forward);
        var worldUp = Vector3.UnitZ;

        var right = Vector3.Normalize(Vector3.Cross(worldUp, forward));
        if (right.LengthSquared() < 1e-4f)
        {
            // Looking straight up/down: fallback to camera's right vector.
            right = Vector3.Normalize(camera.Right);
        }

        var up = Vector3.Cross(forward, right);

        var cameraRotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            0, 0, 0, 1
        ));

        // Apply a fixed viewmodel-space rotation to match the expected model orientation.
        var viewmodelOffsetRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -float.DegreesToRadians(90))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -float.DegreesToRadians(90));
        var viewmodelRotation = Quaternion.Normalize(cameraRotation * viewmodelOffsetRot);


        var bobInputRotation = Quaternion.Inverse(viewmodelRotation);

        var targetBob = Vector3.Transform(input.Velocity * 0.005f, bobInputRotation);

        targetBob.Y = -targetBob.Y; // switch sideways movement to be leading instead of trailing
        targetBob.Z = MathF.Abs(targetBob.Z);
        targetBob.Y *= 0.3f; // dampen sideways movement
        targetBob.Z *= 0.3f; // dampen vertical movement

        // Smooth bob transitions to avoid harsh changes
        currentBob = Vector3.Lerp(currentBob, targetBob, 0.5f);

        // Add walking bob based on uptime
        var speed = input.Velocity.Length();
        var bobAmplitude = MathUtils.Saturate((speed - 150f) / 150f) * 0.1f;

        if (!input.PlayerMovement.OnGround)
        {
            bobAmplitude = 0;
        }

        var bobFrequency = 18; //float.Lerp(10f, 20f, bobAmplitude);
        var walkBob = new Vector3(1, 0.5f, 1) * MathF.Sin(uptime * bobFrequency) * bobAmplitude;

        var rotationMatrix = Matrix4x4.CreateFromQuaternion(viewmodelRotation);
        var offset = Vector3.Transform(ViewmodelOffset - currentBob - walkBob, viewmodelRotation);

        TargetTransform = rotationMatrix with { Translation = camera.Location + offset };

        // Keep legs oriented with player yaw only (ignore camera pitch) to avoid pitch-tilt on leg model.
        var playerYawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, camera.Yaw);
        var playerRotation = Quaternion.Normalize(playerYawRotation);
        PlayerTransform = Matrix4x4.CreateFromQuaternion(playerRotation) * Matrix4x4.CreateTranslation(input.PlayerMovement.Position);

        if (Legs?.AnimationController is { } legsController)
        {
            var crouched = input.PlayerMovement.CrouchBlend;
            var standing = 1f - crouched;

            Vector2 walkRun = new(float.Lerp(84f, 120f, standing), 250f);

            var running = MathUtils.Saturate((speed - walkRun.X) / (walkRun.Y - walkRun.X));
            var walking = MathUtils.Saturate(speed / walkRun.X) * (1f - running);
            var stopped = MathF.Max(0f, 1f - running - walking);

            var jumping = 0f;
            var justJumped = false;

            if (!input.PlayerMovement.OnGround)
            {
                jumping = 1f;
                running = 0f;
                walking = 0f;
                stopped = 0f;

                justJumped = input.PlayerMovement.WasOnGroundLastFrame;

                if (justJumped)
                {
                    legsController.SetAnimationProperties(GetThirdpersonAnim(Posture.Standing, MovementState.Jumping), 0f, looping: false);
                    legsController.SetAnimationProperties(GetThirdpersonAnim(Posture.Crouching, MovementState.Jumping), 0f, looping: false);
                }
            }

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

            var headingWeights = new Dictionary<Heading, float>();
            var headingTotal = 0f;
            foreach (var heading in HeadingVectors.Keys)
            {
                var weight = MathF.Max(0f, Vector2.Dot(currentWalkDirection, HeadingVectors[heading]));
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
                var t = posture == Posture.Standing ? standing : crouched;

                legsController.SetAnimationWeight(GetThirdpersonAnim(posture, MovementState.Stopped), stopped * t);
                legsController.SetAnimationWeight(GetThirdpersonAnim(posture, MovementState.Jumping), jumping * t);
            }


            Span<(Posture, MovementState)> locomotionStates = [
                (Posture.Crouching, MovementState.Walking), // crouch
                (Posture.Standing, MovementState.Walking), // walk
                (Posture.Standing, MovementState.Running), // run
            ];

            // 8 way blend
            foreach (var heading in HeadingVectors.Keys)
            {
                var headingWeight = headingWeights.TryGetValue(heading, out var w) ? w : 0f;

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

        var (fireDelay, altFireDelay) = GetWeaponFireDelays();

        var requestedFire = SelectedItemIndex == 2
            ? input.Pressed(TrackedKeys.MouseLeft)
            : input.Holding(TrackedKeys.MouseLeft);

        if (requestedFire && attackCooldown <= 0f)
        {
            SetState(AnimationState.Attack);
            attackCooldown = fireDelay;
            if (SelectedItemIndex != 3 && muzzleFlashParticle != null)
            {
                muzzleFlashParticle.GetControlPoint(1).Position = Vector3.One; // light radius
                muzzleFlashParticle.Restart();
            }
        }
        else if (input.Holding(TrackedKeys.MouseRight) && alternateAttackCooldown <= 0f)
        {
            SetState(AnimationState.AlternateAttack);
            alternateAttackCooldown = altFireDelay;
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
        else if (input.Pressed(TrackedKeys.Q))
        {
            SelectPreviousItem();
        }

        if (input.Pressed(TrackedKeys.F))
        {
            SetState(AnimationState.LookAt);
        }
    }

    /// <summary>
    /// Update
    /// </summary>
    public override void Update(Scene.UpdateContext context)
    {
        Transform = TargetTransform;

        if (!FirstPersonMode)
        {
            Transform *= Matrix4x4.CreateScale(0);
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

        var active = AnimationController.ActiveAnimation;
        if (active != null)
        {
            var frame = AnimationController.Frame;

            if (State != AnimationState.Idle && AnimationController.ActiveClipFinished)
            {
                SetState(AnimationState.Idle);
            }

            PrimarySkeletonDebug.Transform = Transform;
        }

        base.Update(context);

        // LocalBoundingBox = new AABB(Vector3.Zero, float.PositiveInfinity);

        static void UpdateItem(ModelSceneNode item, Scene.UpdateContext context, AABB bounds)
        {
            item.Update(context);
            item.LocalBoundingBox = bounds;
            item.Scene.DynamicOctree.Update(item, bounds);
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

                var ag2Controller = AnimationController.CurrentSubController;

                if (ag2Controller == null)
                {
                    continue;
                }

                var wpnIndex = ag2Controller.Value.Skeleton.GetBoneIndex("wpn");

                if (wpnIndex == -1)
                {
                    // context.TextRenderer.AddTextRelative("not found", 0.5f, 0.5f, 13, Color32.Blue, context.Camera);
                    continue;
                }

                var wpnTransform = ag2Controller.Value.Handler.Pose[wpnIndex];

                item.Transform = wpnTransform * Transform;
                UpdateItem(item, context, LocalBoundingBox);

                // Update muzzle flash particle transform to wpnTip bone
                if (muzzleFlashParticle != null && isSelected)
                {
                    var wpnTipIndex = ag2Controller.Value.Skeleton.GetBoneIndex("wpnTip");
                    if (wpnTipIndex != -1)
                    {
                        var wpnTipTransform = ag2Controller.Value.Handler.Pose[wpnTipIndex];
                        muzzleFlashParticle.Transform = wpnTipTransform * Transform;
                    }
                }
            }
        }
    }
}
