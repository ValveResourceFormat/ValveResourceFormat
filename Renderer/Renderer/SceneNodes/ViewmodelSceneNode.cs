
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>
/// Animgraph 2 model node.
/// </summary>
public class ViewmodelSceneNode : ModelSceneNode
{
    /// <summary>
    /// Toggle rendering.
    /// </summary>
    public bool Visible { get; set; }

    /// <summary>
    /// Viewmodel offset in viewmodel space (forward, right, up).
    /// </summary>
    public Vector3 ViewmodelOffset { get; set; } = new Vector3(0, -2, -2);

    /// <summary>
    /// The player arms.
    /// </summary>
    public ModelSceneNode Arms => this;

    public ModelSceneNode? Legs { get; set; }

    readonly List<ModelSceneNode?> Items = [];

    ModelSceneNode? SelectedItem => Items.ElementAtOrDefault(SelectedSlot - 1);

    private int PreviousSelectedItem;

    public int SelectedSlot
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            PreviousSelectedItem = field;
            field = value;
            OnSelectedItemChanged();
        }
    } = 3;

    SkeletonSceneNode PrimarySkeletonDebug;
    ParticleSceneNode? muzzleFlashParticle;

    private Matrix4x4 TargetTransform = Matrix4x4.Identity;
    private Matrix4x4 PlayerTransform = Matrix4x4.Identity;
    private float attackCooldown;
    private float alternateAttackCooldown;
    private Vector3 currentBob = Vector3.Zero;

    private Vector2 currentWalkDirection = new(0, 1);
    private const float WalkDirectionLerpRate = 0.35f;

    /// <summary>
    /// Selects the previously selected item (used for quick weapon switching).
    /// </summary>
    public void SelectPreviousItem()
    {
        SelectedSlot = PreviousSelectedItem;
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
        Center,
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
        [Heading.Center] = new(0, 0),
    };

    private static string GetThirdpersonAnim(Posture posture, MovementState movement, Heading heading = Heading.Center)
    {
        const string item = "rifle";

        if (movement == MovementState.Jumping)
        {
            return posture == Posture.Standing ? "jump_stand" : "sh_jump_crouched_stand";
        }

        var run = posture == Posture.Crouching
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
            Heading.Center => "center",
            _ => throw new ArgumentOutOfRangeException(nameof(heading), heading, null)
        };

        if (movement == MovementState.Stopped)
        {
            direction = "stopped";
        }

        var anim = $"{run}_new_{item}_{direction}";

        // const string walkRifleStopped = "walk_new_rifle_stopped";
        // const string crouchRifleStopped = "crouch_new_rifle_stopped";
        // const string walkKnifeN = "walk_new_rifle_n";
        // const string crouchKnifeN = "crouch_new_rifle_n";

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
            if (ItemAnimations.TryGetValue(SelectedSlot, out var anim))
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
        => SelectedSlot switch
        {
            1 => (0.1f, 2f),
            2 => (0.1f, 2f),
            3 => (0.3f, 1f),
            _ => (0.1f, 2f),
        };

    void SetState(AnimationState newState)
    {
        if (true)
        {
            State = newState;
            var looping = newState == AnimationState.Idle;

            var timeScale = 1f; // 0.3f;

            var fadeIn = newState is AnimationState.Draw or AnimationState.Attack ? 0f : 0.35f;

            AnimationController.IsPaused = false;
            AnimationController.Looping = looping;
            AnimationController.FrametimeMultiplier = timeScale;
            SetAnimationByName(TargetAnimation, fadeIn);

            SelectedItem?.AnimationController.IsPaused = false;
            SelectedItem?.AnimationController.Looping = looping;
            SelectedItem?.AnimationController.FrametimeMultiplier = timeScale;
            SelectedItem?.SetAnimationByName(TargetAnimation + ".secondary_0", fadeIn);
        }
    }

    private void OnSelectedItemChanged()
    {
        SetState(AnimationState.Draw);
    }

    internal ViewmodelSceneNode(Scene scene, Model model)
        : base(scene, model, null, true)
    {
        SetState(AnimationState.Idle);
        TargetTransform = Transform;

        var ag2Controller = AnimationController.CurrentSubController!.Value.Handler;
        PrimarySkeletonDebug = new SkeletonSceneNode(Scene, ag2Controller, ag2Controller.Skeleton)
        {
            LayerName = "world_layer_base",
            Flags = ObjectTypeFlags.DisableVisCulling,
            Enabled = false,
        };

        Scene.Add(PrimarySkeletonDebug, true);

        Legs = new ModelSceneNode(Scene, model)
        {
            LayerName = "world_layer_base",
            Flags = ObjectTypeFlags.DisableVisCulling,
        };
        Scene.Add(Legs, true);

        Legs.AnimationController.TwistConstraints = [];
        Legs.IsFirstpersonLegs = true;

        // Load additional animations from animset_ct
        LoadAnimsetAnimations(Legs);
        Legs.AnimationController.Looping = true;

        foreach (var posture in Enum.GetValues<Posture>())
        {
            foreach (var movement in Enum.GetValues<MovementState>())
            {
                foreach (var heading in Enum.GetValues<Heading>())
                {
                    Legs.SetAnimationByName(GetThirdpersonAnim(posture, movement, heading), -1);
                }
            }
        }
    }

    private void LoadAnimsetAnimations(ModelSceneNode legs)
    {
        var animsetResource = Scene.RendererContext.FileLoader.LoadFileCompiled("characters/models/shared/animsets/animset_ct.vmdl");
        if (animsetResource?.DataBlock is not Model animsetModel)
        {
            return;
        }

        legs.Animations.AddRange(Model.LoadEmbeddedAnimationsWithSkeleton(Scene.RendererContext.FileLoader, legs.AnimationController.Skeleton, animsetModel));
    }

    record struct Anim(string Idle, string Draw, string LookAt, string Attack, string? AltAttack = null, string? Attack2 = null, string? AltAttack2 = null);

    Dictionary<int, Anim> ItemAnimations = new()
    {
        [1] = new Anim(
            "rifle/rifle_m4a4/idle_m4a4.vnmclip",
            "rifle/rifle_m4a4/draw_m4a4.vnmclip",
            "rifle/rifle_m4a4/lookat01_m4a4.vnmclip",
            "rifle/rifle_m4a4/shoot1_m4a4.vnmclip",
            "rifle/rifle_m4a4/reload_m4a4.vnmclip"
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
            LayerName = "world_layer_base",
            Flags = ObjectTypeFlags.DisableVisCulling,
        };
        Scene.Add(model, true);
        Items.Add(model);

        model.Parent = this;

        foreach (var anim in Animations)
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
    /// Try to load the CS2 viewmodel, returning null when the required resources are not found.
    /// </summary>
    /// <param name="scene"></param>
    /// <returns></returns>
    public static ViewmodelSceneNode? TryLoadCs2Viewmodel(Scene scene)
    {
        var loader = scene.RendererContext.FileLoader;

        Span<string> resources = [
            "phase2/characters/models/ctm_st6/ctm_st6_varianti_ag2.vmdl",
            "weapons/models/m4a4/weapon_rif_m4a4.vmdl",
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

        viewmodel.Arms.SetActiveMeshGroups([
            "first_or_third_person_@2_#&firstperson_default"
        ]);

        viewmodel.SelectedSlot = 2;
        viewmodel.SelectedSlot = 3;
        viewmodel.LayerName = "world_layer_base";
        viewmodel.Flags |= ObjectTypeFlags.DisableVisCulling;

        // Load muzzle flash particle
        var muzzleFlashResource = loader.LoadFileCompiled("particles/unified_weapon_fx/uweapon_muzflsh_riffle.vpcf");
        if (muzzleFlashResource?.DataBlock is ParticleSystem particleSystem)
        {
            viewmodel.muzzleFlashParticle = new ParticleSceneNode(scene, particleSystem)
            {
                LayerName = "world_layer_base",
                Flags = ObjectTypeFlags.DisableVisCulling,
            };
            scene.Add(viewmodel.muzzleFlashParticle, true);
        }

        scene.RendererContext.Logger.LogInformation($"Loaded viewmodel");

        scene.Add(viewmodel, true);
        return viewmodel;
    }

    /// <summary>
    /// Process input for the viewmodel, updating its transform to match the camera's orientation and position.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="uptime"></param>
    public void ProcessInput(UserInput input, float uptime)
    {
        Visible = true;

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
                    desiredWalkDir = Vector2.Normalize(camRelative);
                }
            }

            if (desiredWalkDir.LengthSquared() > 1e-6f)
            {
                currentWalkDirection = Vector2.Lerp(currentWalkDirection, desiredWalkDir, WalkDirectionLerpRate);
            }

            if (currentWalkDirection.LengthSquared() < 1e-6f)
            {
                currentWalkDirection = new(0, 1);
            }

            var headingWeights = new Dictionary<Heading, float>();
            var headingTotal = 0f;
            foreach (var heading in HeadingVectors.Keys.Where(h => h != Heading.Center))
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

            foreach (var heading in HeadingVectors.Keys.Where(h => h != Heading.Center))
            {
                var headingWeight = headingWeights.TryGetValue(heading, out var w) ? w : 0f;

                legsController.SetAnimationWeight(GetThirdpersonAnim(Posture.Crouching, MovementState.Walking, heading), walking * crouched * headingWeight);
                legsController.SetAnimationWeight(GetThirdpersonAnim(Posture.Standing, MovementState.Walking, heading), walking * standing * headingWeight);

                // crouch cannot run
                legsController.SetAnimationWeight(GetThirdpersonAnim(Posture.Standing, MovementState.Running, heading), running * standing * headingWeight);
            }
        }

        var (fireDelay, altFireDelay) = GetWeaponFireDelays();

        var requestedFire = SelectedSlot == 2
            ? input.Pressed(TrackedKeys.MouseLeft)
            : input.Holding(TrackedKeys.MouseLeft);

        if (requestedFire && attackCooldown <= 0f)
        {
            SetState(AnimationState.Attack);
            attackCooldown = fireDelay;
            muzzleFlashParticle?.Restart();
        }
        else if (input.Holding(TrackedKeys.MouseRight) && alternateAttackCooldown <= 0f)
        {
            SetState(AnimationState.AlternateAttack);
            alternateAttackCooldown = altFireDelay;
        }
        else if (input.Pressed(TrackedKeys.Slot1))
        {
            SelectedSlot = 1;
        }
        else if (input.Pressed(TrackedKeys.Slot2))
        {
            SelectedSlot = 2;
        }
        else if (input.Pressed(TrackedKeys.Slot3))
        {
            SelectedSlot = 3;
        }
        else if (input.Pressed(TrackedKeys.Up))
        {
            SelectPreviousItem();
        }
        else if (input.Pressed(TrackedKeys.F))
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

        Legs?.Transform = PlayerTransform;

        attackCooldown = MathF.Max(0f, attackCooldown - context.Timestep);
        alternateAttackCooldown = MathF.Max(0f, alternateAttackCooldown - context.Timestep);

        var active = AnimationController.ActiveAnimation;
        if (active != null)
        {
            var frame = AnimationController.Frame;

            if (State != AnimationState.Idle && AnimationController.IsPaused)
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
            var isSelected = i == SelectedSlot;
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