using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimGraphNodes;

// CS2 third person aiming: spreads the aim yaw and pitch over the spine, places the weapon per
// weapon category, and solves both hands onto it.
sealed class AimCSNode : PassthroughNode
{
    public short VerticalAngleNodeIdx { get; }
    public short HorizontalAngleNodeIdx { get; }
    public short WeaponCategoryNodeIdx { get; }
    public short WeaponTypeNodeIdx { get; }
    public short WeaponActionNodeIdx { get; }
    public short WeaponDropNodeIdx { get; }
    public short IsDefusingNodeIdx { get; }
    public short CrouchWeightNodeIdx { get; }
    public float HandIKBlendInTimeSeconds { get; }
    public float ActionBlendTimeSeconds { get; }
    public float PlantingBlendTimeSeconds { get; }

    public AimCSNode(KVObject data) : base(data)
    {
        VerticalAngleNodeIdx = data.GetInt16Property("m_nVerticalAngleNodeIdx");
        HorizontalAngleNodeIdx = data.GetInt16Property("m_nHorizontalAngleNodeIdx");
        WeaponCategoryNodeIdx = data.GetInt16Property("m_nWeaponCategoryNodeIdx");
        WeaponTypeNodeIdx = data.GetInt16Property("m_nWeaponTypeNodeIdx");
        WeaponActionNodeIdx = data.GetInt16Property("m_nWeaponActionNodeIdx");
        WeaponDropNodeIdx = data.GetInt16Property("m_nWeaponDropNodeIdx");
        IsDefusingNodeIdx = data.GetInt16Property("m_nIsDefusingNodeIdx");
        CrouchWeightNodeIdx = data.GetInt16Property("m_nCrouchWeightNodeIdx");
        HandIKBlendInTimeSeconds = data.GetFloatProperty("m_flHandIKBlendInTimeSeconds");
        ActionBlendTimeSeconds = data.GetFloatProperty("m_flActionBlendTimeSeconds");
        PlantingBlendTimeSeconds = data.GetFloatProperty("m_flPlantingBlendTimeSeconds");
    }

    static readonly GlobalSymbol ActionEnding = new("WPN_IK_ACTION_ENDING");
    static readonly GlobalSymbol DisableHandIK = new("WPN_DISABLE_HAND_IK");
    static readonly GlobalSymbol ActionReload = new("action_reload");
    static readonly GlobalSymbol ActionDeploy = new("action_deploy");
    static readonly GlobalSymbol ActionSilencerAttach = new("action_silencer_attach");
    static readonly GlobalSymbol ActionSilencerDetach = new("action_silencer_detach");
    static readonly GlobalSymbol ActionC4Plant = new("action_c4_plant");

    FloatValueNode? HorizontalAngleNode;
    FloatValueNode? VerticalAngleNode;
    IDValueNode? WeaponCategoryNode;
    IDValueNode? WeaponTypeNode;
    FloatValueNode? CrouchWeightNode;
    IDValueNode? WeaponActionNode;
    FloatValueNode? WeaponDropNode;
    BoolValueNode? IsDefusingNode;

    GlobalSymbol weaponCategory;
    GlobalSymbol weaponType;
    BlendWeight plantBlend = new();
    BlendWeight handIKBlend = new();
    BlendWeight actionBlend = new();

    AimCSSolver solver;

    public override void Instantiate(GraphContext ctx)
    {
        base.Instantiate(ctx);
        ctx.SetOptionalNodeFromIndex(HorizontalAngleNodeIdx, ref HorizontalAngleNode);
        ctx.SetOptionalNodeFromIndex(VerticalAngleNodeIdx, ref VerticalAngleNode);
        ctx.SetOptionalNodeFromIndex(WeaponCategoryNodeIdx, ref WeaponCategoryNode);
        ctx.SetOptionalNodeFromIndex(WeaponTypeNodeIdx, ref WeaponTypeNode);
        ctx.SetOptionalNodeFromIndex(CrouchWeightNodeIdx, ref CrouchWeightNode);
        ctx.SetOptionalNodeFromIndex(WeaponActionNodeIdx, ref WeaponActionNode);
        ctx.SetOptionalNodeFromIndex(WeaponDropNodeIdx, ref WeaponDropNode);
        ctx.SetOptionalNodeFromIndex(IsDefusingNodeIdx, ref IsDefusingNode);

        solver = new AimCSSolver(ctx.Skeleton, PoseTransforms.Length);
    }

    protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
    {
        base.InitializeInternal(ctx, initialTime);

        weaponCategory = default;
        weaponType = default;

        HorizontalAngleNode?.Initialize(ctx);
        VerticalAngleNode?.Initialize(ctx);
        WeaponCategoryNode?.Initialize(ctx);
        WeaponTypeNode?.Initialize(ctx);
        WeaponActionNode?.Initialize(ctx);
        WeaponDropNode?.Initialize(ctx);
        CrouchWeightNode?.Initialize(ctx);
        IsDefusingNode?.Initialize(ctx);

        actionBlend.EasingOperation = EasingOperation.InOutQuad;
        UpdateState(ctx, isInitialization: true);
    }

    protected override void ShutdownInternal(GraphContext ctx)
    {
        IsDefusingNode?.Shutdown(ctx);
        CrouchWeightNode?.Shutdown(ctx);
        WeaponDropNode?.Shutdown(ctx);
        WeaponActionNode?.Shutdown(ctx);
        WeaponTypeNode?.Shutdown(ctx);
        WeaponCategoryNode?.Shutdown(ctx);
        VerticalAngleNode?.Shutdown(ctx);
        HorizontalAngleNode?.Shutdown(ctx);
        base.ShutdownInternal(ctx);
    }

    bool UpdateState(GraphContext ctx, bool isInitialization)
    {
        var actionEnding = false;
        var handIKDisabled = false;

        for (var i = 0; i < ctx.SampledEvents.Count && !(actionEnding && handIKDisabled); i++)
        {
            var id = ctx.SampledEvents[i].ID;
            actionEnding |= id == ActionEnding;
            handIKDisabled |= id == DisableHandIK;
        }

        var action = WeaponActionNode?.GetValue(ctx) ?? default;
        var isAction = !actionEnding && (action == ActionReload || action == ActionDeploy || action == ActionSilencerAttach || action == ActionSilencerDetach);
        var isPlanting = action == ActionC4Plant;
        var isHandIKOn = !handIKDisabled && !(IsDefusingNode?.GetValue(ctx) ?? false);

        if (isInitialization)
        {
            actionBlend.Reset(ActionBlendTimeSeconds, isAction);
            handIKBlend.Reset(HandIKBlendInTimeSeconds, isHandIKOn);
            plantBlend.Reset(PlantingBlendTimeSeconds, !isPlanting);
        }
        else
        {
            if (isHandIKOn)
            {
                handIKBlend.Update(ctx.DeltaTime, true);
            }
            else
            {
                // Switching off is instant, and leaves the blend time at a single frame for later blend ins
                handIKBlend.Reset(ctx.DeltaTime, false);
            }

            plantBlend.Update(ctx.DeltaTime, !isPlanting);
            actionBlend.Update(ctx.DeltaTime, isAction);
        }

        weaponCategory = WeaponCategoryNode?.GetValue(ctx) ?? default;
        weaponType = WeaponTypeNode?.GetValue(ctx) ?? default;
        return weaponCategory.IsValid && weaponType.IsValid;
    }

    static float NormalizeDegrees180(float angle) => angle - (MathF.Floor((angle / 360f) + 0.5f) * 360f);

    public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
    {
        var result = base.Update(ctx, updateRange);

        if (ChildNode is not { IsValid: true })
        {
            return result;
        }

        if (!UpdateState(ctx, isInitialization: false))
        {
            return result;
        }

        var plantWeight = plantBlend.GetWeight();
        var parameters = new AimCSSolver.Parameters
        {
            Yaw = Math.Clamp(NormalizeDegrees180((HorizontalAngleNode?.GetValue(ctx) ?? 0f) * plantWeight), -89f, 89f),
            Pitch = Math.Clamp(NormalizeDegrees180((VerticalAngleNode?.GetValue(ctx) ?? 0f) * plantWeight), -89f, 89f),
            Crouch = Math.Clamp(CrouchWeightNode?.GetValue(ctx) ?? 0f, 0f, 1f),
            ActionWeight = actionBlend.GetWeight(),
            Drop = Math.Clamp(WeaponDropNode?.GetValue(ctx) ?? 0f, -10f, 10f),
            HandIKWeight = handIKBlend.GetWeight(),
        };

        AimCSSolver.MapWeapon(weaponCategory, weaponType, out parameters.Category, out parameters.Type);

        if (solver.Solve(result.Pose, parameters, PoseTransforms))
        {
            result.Pose = PoseTransforms;
        }

        return result;
    }
}

sealed class AimCSSolver
{
    public struct Parameters
    {
        public float Yaw;
        public float Pitch;
        public float Drop;
        public float HandIKWeight;
        public float ActionWeight;
        public float Crouch;
        public byte Category;
        public byte Type;
    }

    enum Category : byte
    {
        None = 0,
        Knife = 1,
        Pistol = 2,
        Rifle = 3,
        Grenade = 4,
        Equipment = 5,
    }

    struct Settings
    {
        public Vector2 YawRange;
        public Vector2 SpinePitchRange;
        public Vector4 YawWeights;
        public Vector4 PitchWeights;
        public Vector2 WeaponPitchRange;
        public Vector2 HeadPitchRange;
        public Vector2 HeadSplit;
        public Vector2 OffsetPitchRange;
        public Vector3 OffsetLow;
        public Vector3 OffsetHigh;
        public bool LeftHandIK;
    }

    readonly Pose pose;
    readonly bool hasBones;
    readonly int spine0, spine1, spine2, spine3, neck0, head0;
    readonly int weaponPivot, weapon, weaponTip, weaponEnd, weaponHandLeft, weaponHandRight, handLeft, handRight;

    // The spine to head chain, spine_0 first
    readonly int[] chain = new int[6];
    readonly Transform[] chainModel = new Transform[6];

    public AimCSSolver(Skeleton skeleton, int boneCount)
    {
        pose = new Pose(skeleton);

        int Bone(string name) => skeleton.GetBoneIndex(new GlobalSymbol(name));

        var pelvis = Bone("pelvis");
        spine0 = Bone("spine_0");
        spine1 = Bone("spine_1");
        spine2 = Bone("spine_2");
        spine3 = Bone("spine_3");
        neck0 = Bone("neck_0");
        head0 = Bone("head_0");
        weaponPivot = Bone("wpnPivot");
        weapon = Bone("wpn");
        weaponTip = Bone("wpnTip");
        weaponEnd = Bone("wpnEnd");
        weaponHandLeft = Bone("wpnHand_L");
        weaponHandRight = Bone("wpnHand_R");
        var armUpperLeft = Bone("arm_upper_L");
        var armUpperRight = Bone("arm_upper_R");
        handLeft = Bone("hand_L");
        handRight = Bone("hand_R");

        hasBones = Math.Min(Math.Min(Math.Min(pelvis, spine0), Math.Min(spine1, spine2)), Math.Min(Math.Min(spine3, neck0), head0)) != -1
            && Math.Min(Math.Min(Math.Min(weaponPivot, weapon), Math.Min(weaponTip, weaponEnd)), Math.Min(weaponHandLeft, weaponHandRight)) != -1
            && Math.Min(Math.Min(armUpperLeft, armUpperRight), Math.Min(handLeft, handRight)) != -1;

        if (!hasBones)
        {
            return;
        }

        // The chain is found by walking the parents of the head
        var bone = head0;
        for (var k = 5; k >= 0; k--)
        {
            chain[k] = bone;
            bone = skeleton.GetParentBoneIndex(bone);
            if (bone == -1 && k > 0)
            {
                hasBones = false;
                return;
            }
        }
    }

    public static void MapWeapon(GlobalSymbol category, GlobalSymbol type, out byte categoryId, out byte typeId)
    {
        static bool Is(GlobalSymbol symbol, string name) => symbol == GlobalSymbol.Lookup(name);

        var c = Category.None;
        byte t = 0;

        if (Is(category, "weapon_category_knife"))
        {
            c = Category.Knife;
        }
        else if (Is(category, "weapon_category_pistol"))
        {
            c = Category.Pistol;
        }
        else if (Is(category, "weapon_category_smg"))
        {
            c = Category.Pistol;

            if (Is(type, "weapon_mp5sd"))
            {
                t = 1;
            }
            else if (Is(type, "weapon_mp9"))
            {
                (c, t) = (Category.Rifle, 2);
            }
            else if (Is(type, "weapon_p90"))
            {
                (c, t) = (Category.Rifle, 3);
            }
            else if (Is(type, "weapon_bizon"))
            {
                (c, t) = (Category.Rifle, 4);
            }
            else if (Is(type, "weapon_ump45"))
            {
                c = Category.Rifle;
            }
        }
        else if (Is(category, "weapon_category_shotgun"))
        {
            c = Category.Rifle;
            t = Is(type, "weapon_sawedoff") ? (byte)5 : Is(type, "weapon_mag7") ? (byte)6 : (byte)0;
        }
        else if (Is(category, "weapon_category_rifle") || Is(category, "weapon_category_sniper"))
        {
            c = Category.Rifle;
            t = Is(type, "weapon_famas") ? (byte)7 : (byte)0;
        }
        else if (Is(category, "weapon_category_machinegun"))
        {
            (c, t) = (Category.Rifle, 8);
        }
        else if (Is(category, "weapon_category_grenade"))
        {
            c = Category.Grenade;
        }
        else if (Is(category, "weapon_category_equipment"))
        {
            c = Category.Equipment;
            t = Is(type, "weapon_c4") ? (byte)9 : (byte)0;
        }

        categoryId = (byte)c;
        typeId = t;
    }

    static Settings CreateSettings(Parameters p)
    {
        var s = new Settings
        {
            YawRange = new(-55f, 65f),
            SpinePitchRange = new(-12.5f, 10f),
            YawWeights = new(0.1f, 0.2f, 0.3f, 0.4f),
            PitchWeights = new(0.125f, 0.225f, 0.25f, 0.4f),
            WeaponPitchRange = new(-90f, 90f),
            HeadPitchRange = new(-45f, 35f),
            HeadSplit = new(0.35f, 0.65f),
            OffsetPitchRange = new(-90f, 90f),
            LeftHandIK = true,
        };

        var c = p.Crouch;

        switch ((Category)p.Category)
        {
            case Category.Rifle:
                s.SpinePitchRange = new(-25.5f, 25f);
                s.HeadPitchRange = new(-35f, 20f);
                s.WeaponPitchRange = new((-15f * c) - 75f, 80f);
                s.OffsetPitchRange = new(-30f, 50f);
                s.OffsetLow = new(-10f, 3f, -5f);
                s.OffsetHigh = new(-1f - (7f * c), 3f, 4f + (8f * c));

                switch (p.Type)
                {
                    case 2:
                        s.OffsetHigh = new(2f, 0f, 4f);
                        break;
                    case 3:
                        s.OffsetLow = Vector3.Zero;
                        s.OffsetHigh = new(3f, 2f, 4f);
                        break;
                    case 4:
                        s.OffsetHigh = new(-6f, 2f, 6f);
                        break;
                    case 5:
                        s.OffsetHigh = new(6f, 2f, 4f);
                        break;
                    case 6:
                        s.OffsetLow = new(-2f, 0f, 0f);
                        s.OffsetHigh = Vector3.Zero;
                        break;
                    case 7:
                        s.OffsetLow = new(-10f, 3f, -5f);
                        s.OffsetHigh = new(-5f, 1f, 3f);
                        break;
                    case 8:
                        s.OffsetLow = new(-6f, 4f, -16f);
                        s.OffsetHigh = new(-8f, 0f, 14f);
                        s.OffsetPitchRange.Y = 20f;
                        break;
                }

                break;

            case Category.Pistol:
                s.SpinePitchRange = new((-10f * c) - 25.5f, 10f);
                s.WeaponPitchRange = new(-40f, 55f);
                s.HeadPitchRange = new((10f * c) - 35f, 35f);
                s.OffsetPitchRange = new(-70f, 50f);
                s.OffsetLow = Vector3.Zero;
                s.OffsetHigh = p.Type == 1 ? new(-2f, 0f, 4f) : new(0f, 0f, 1f);
                break;

            case Category.Knife:
                s.WeaponPitchRange = new(-35f, 25f);
                s.SpinePitchRange = new(-15.5f, 25f);
                s.HeadPitchRange = new(-35f, 20f);
                s.LeftHandIK = false;
                break;

            case Category.Equipment:
                s.WeaponPitchRange = new(-15f, 5f);
                s.SpinePitchRange = new(-15.5f, 15f);
                s.HeadPitchRange = new(-35f, 20f);
                s.LeftHandIK = p.Type == 9;
                break;

            case Category.Grenade:
                s.LeftHandIK = false;
                break;
        }

        return s;
    }

    // Negative values map onto [min, 0] and positive ones onto [0, max], both over 90 degrees
    static float Remap(float value, Vector2 range)
    {
        if (MathF.Abs(range.X - range.Y) < float.Epsilon)
        {
            return range.X;
        }

        if (value < 0f)
        {
            return MathF.Abs(range.X) < float.Epsilon ? 0f : float.Lerp(range.X, 0f, MathUtils.Saturate((value + 90f) / 90f));
        }

        return MathF.Abs(range.Y) < float.Epsilon ? 0f : float.Lerp(0f, range.Y, MathUtils.Saturate(value / 90f));
    }

    static Quaternion AxisAngle(Vector3 axis, float degrees) => Quaternion.CreateFromAxisAngle(axis, float.DegreesToRadians(degrees));

    // Valve composes A o B as B expressed in A's space
    static Transform Concat(Transform a, Transform b) => b * a;

    Transform Local(int bone) => pose.GetTransform(bone);

    public bool Solve(ReadOnlySpan<Transform> inputPose, Parameters p, Span<Transform> outputPose)
    {
        if (!hasBones)
        {
            return false;
        }

        pose.SetParentSpaceTransforms(inputPose);

        var s = CreateSettings(p);

        // Effective pitch, pulled towards level during actions
        var pitch = p.Pitch;
        if (p.ActionWeight > 0f)
        {
            var a = 40f * p.ActionWeight;
            pitch = float.Lerp(a - 90f, 90f - a, MathUtils.Saturate((p.Pitch + 90f) / 180f));
        }

        // Undoes the weapon's own rotation when the pivot takes over the weapon's model rotation
        var weaponRotationFix = Quaternion.Normalize(Quaternion.Conjugate(Concat(Local(weaponPivot), Local(weapon)).Angle) * Local(weaponPivot).Angle);

        ApplyWeaponDrop(p, pitch);

        var (yawFraction, pitchFraction, aimDirection) = AimBody(p, s, pitch);

        Transform weaponModel;
        switch ((Category)p.Category)
        {
            case Category.Knife:
            case Category.Equipment:
                weaponModel = PlaceHeldWeapon(s, pitch, yawFraction, pitchFraction);
                break;
            case Category.Pistol:
                weaponModel = PlacePistol(p, s, pitch, yawFraction, pitchFraction, aimDirection, weaponRotationFix);
                break;
            case Category.Rifle:
                weaponModel = PlaceRifle(p, s, pitch, aimDirection, weaponRotationFix);
                break;
            default:
                weaponModel = pose.GetModelSpaceTransform(weapon);
                break;
        }

        // Hands onto the weapon
        var handIKWeight = Math.Clamp(p.HandIKWeight, 0f, 1f);
        if (handIKWeight > 0.001f)
        {
            if (s.LeftHandIK)
            {
                TwoBoneSolver.Solve(pose, handLeft, Concat(weaponModel, Local(weaponHandLeft)), 0f, IKBlendMode.Effector, handIKWeight);
            }

            TwoBoneSolver.Solve(pose, handRight, Concat(weaponModel, Local(weaponHandRight)), 0f, IKBlendMode.Effector, handIKWeight);
        }

        pose.CopyParentSpaceTransformsTo(outputPose);
        return true;
    }

    void ApplyWeaponDrop(Parameters p, float pitch)
    {
        var drop = p.Drop * (1f - MathUtils.Saturate(MathF.Abs(pitch) / 70f)) * Math.Clamp(1f - p.ActionWeight, 0f, 1f);
        if (MathF.Abs(drop) <= 0.001f)
        {
            return;
        }

        var pivot = Local(weaponPivot);
        var weaponModel = Concat(pivot, Local(weapon));

        // Offset in model space, not rotated
        weaponModel.Position += new Vector3(-0.75f * MathF.Abs(drop), 0f, -drop);
        pose.SetTransform(weapon, Concat(pivot.Inverse(), weaponModel));
    }

    (float YawFraction, float PitchFraction, Vector3 AimDirection) AimBody(Parameters p, Settings s, float pitch)
    {
        var parentModel = pose.GetModelSpaceTransform(pose.Skeleton.GetParentBoneIndex(chain[0]));

        void RecomputeChain(int from, int to)
        {
            for (var k = from; k <= to; k++)
            {
                chainModel[k] = Concat(k == 0 ? parentModel : chainModel[k - 1], Local(chain[k]));
            }
        }

        RecomputeChain(0, 5);

        var yawFraction = 0f;
        var pitchFraction = 0f;
        var aimYaw = p.Yaw;

        // Yaw around the model up axis, spread over the spine
        if (MathF.Abs(p.Yaw) > 0.001f)
        {
            var yaw = Remap(p.Yaw, s.YawRange);
            aimYaw = yaw;
            yawFraction = yaw < 0f ? yaw / s.YawRange.X : yaw / s.YawRange.Y;

            for (var k = 0; k < 4; k++)
            {
                chainModel[k].Angle = AxisAngle(Vector3.UnitZ, yaw * s.YawWeights[k]) * chainModel[k].Angle;

                for (var j = k + 1; j < 4; j++)
                {
                    chainModel[j] = Concat(chainModel[j - 1], Local(chain[j]));
                }
            }

            RecomputeLocals();
        }

        // Pitch around each spine bone's local Z axis, then the neck and head
        if (MathF.Abs(pitch) > 0.001f)
        {
            var spinePitch = Remap(pitch, s.SpinePitchRange);
            pitchFraction = spinePitch < 0f ? spinePitch / s.SpinePitchRange.X : spinePitch / s.SpinePitchRange.Y;

            for (var k = 0; k < 4; k++)
            {
                RotateLocal(chain[k], spinePitch * s.PitchWeights[k]);
            }

            var headPitch = Remap(pitch, s.HeadPitchRange);
            RotateLocal(neck0, headPitch * s.HeadSplit.X);
            RotateLocal(head0, headPitch * s.HeadSplit.Y);
        }

        RecomputeChain(0, 5);

        // The aim direction in model space, Source pitch is positive down
        var pitchRadians = float.DegreesToRadians(pitch);
        var yawRadians = float.DegreesToRadians(aimYaw);
        var aimDirection = new Vector3(MathF.Cos(pitchRadians) * MathF.Cos(yawRadians), MathF.Cos(pitchRadians) * MathF.Sin(yawRadians), -MathF.Sin(pitchRadians));

        // The weapon pivot sits under an identity root, so its local transform is the top spine bone
        pose.SetTransform(weaponPivot, chainModel[3]);

        return (yawFraction, pitchFraction, aimDirection);

        void RecomputeLocals()
        {
            chainModel[4] = Concat(chainModel[3], Local(chain[4]));
            chainModel[5] = Concat(chainModel[4], Local(chain[5]));

            var previous = parentModel;
            for (var k = 0; k < 4; k++)
            {
                pose.SetTransform(chain[k], Concat(previous.Inverse(), chainModel[k]));
                previous = chainModel[k];
            }
        }
    }

    void RotateLocal(int bone, float degrees)
    {
        var local = Local(bone);
        local.Angle *= AxisAngle(Vector3.UnitZ, degrees);
        pose.SetTransform(bone, local);
    }

    // Pitches the pivot in model space, then tilts it sideways when aiming off to the side
    Transform PitchAndTiltPivot(float weaponPitch, float pitch, float yawFraction, float pitchFraction)
    {
        var pivot = Local(weaponPivot);
        pivot.Angle = AxisAngle(Vector3.UnitY, weaponPitch) * pivot.Angle;

        var tilt = yawFraction * pitchFraction * (pitch < 0f ? 5f : -15f);
        if (MathF.Abs(tilt) > 0.001f)
        {
            var weaponModel = Concat(pivot, Local(weapon));
            var direction = TransformMath.NormalizeOrZero(weaponModel.Position - pivot.Position);
            pivot.Angle = AxisAngle(TransformMath.NormalizeOrZero(Vector3.Cross(direction, Vector3.UnitZ)), tilt) * pivot.Angle;
        }

        return pivot;
    }

    Transform PlaceHeldWeapon(Settings s, float pitch, float yawFraction, float pitchFraction)
    {
        var pivot = PitchAndTiltPivot(Remap(pitch, s.WeaponPitchRange), pitch, yawFraction, pitchFraction);
        pose.SetTransform(weaponPivot, pivot);
        return Concat(pivot, Local(weapon));
    }

    Transform PlacePistol(Parameters p, Settings s, float pitch, float yawFraction, float pitchFraction, Vector3 aimDirection, Quaternion weaponRotationFix)
    {
        var weaponPitch = Remap(pitch, s.WeaponPitchRange) * (1f - (0.5f * MathUtils.Saturate(p.ActionWeight)));
        var pivot = PitchAndTiltPivot(weaponPitch, pitch, yawFraction, pitchFraction);

        var weaponModel = Concat(pivot, Local(weapon));
        var roll = yawFraction * pitchFraction * (p.Yaw < 0f ? -45f : 45f);
        weaponModel.Angle = AxisAngle(Vector3.UnitZ, roll) * weaponModel.Angle;

        weaponModel = OffsetByPitch(s, pivot, weaponModel, pitch);

        if (p.ActionWeight < 1f)
        {
            weaponModel = AlignToAim(weaponModel, aimDirection, p.ActionWeight);
        }

        pivot.Angle *= weaponRotationFix;
        pose.SetTransform(weaponPivot, pivot);
        pose.SetTransform(weapon, Concat(pivot.Inverse(), weaponModel));
        return weaponModel;
    }

    Transform PlaceRifle(Parameters p, Settings s, float pitch, Vector3 aimDirection, Quaternion weaponRotationFix)
    {
        var pivot = Local(weaponPivot);
        var weaponModel = Concat(pivot, Local(weapon));

        // Pitch around the weapon end's local Y axis
        var end = Concat(weaponModel, Local(weaponEnd));
        var endToWeapon = Concat(end.Inverse(), weaponModel);
        var weaponPitch = Remap(pitch, s.WeaponPitchRange) * (1f - (0.5f * MathUtils.Saturate(p.ActionWeight)));
        end.Angle *= AxisAngle(Vector3.UnitY, weaponPitch);
        weaponModel = Concat(end, endToWeapon);

        weaponModel = OffsetByPitch(s, pivot, weaponModel, pitch);

        if (p.ActionWeight < 1f)
        {
            weaponModel = AlignToAim(weaponModel, aimDirection, p.ActionWeight);
        }

        pivot.Angle *= weaponRotationFix;
        pose.SetTransform(weaponPivot, pivot);
        pose.SetTransform(weapon, Concat(pivot.Inverse(), weaponModel));

        if (pitch > -25f)
        {
            HeadLookAtTip(Concat(weaponModel, Local(weaponTip)), MathUtils.Saturate((pitch + 25f) / 115f));
        }

        return weaponModel;
    }

    // Offsets the weapon in its own axes when looking far up or down
    static Transform OffsetByPitch(Settings s, Transform pivot, Transform weaponModel, float pitch)
    {
        static bool IsZero(Vector3 v) => MathF.Abs(v.X) <= 0.01f && MathF.Abs(v.Y) <= 0.01f && MathF.Abs(v.Z) <= 0.01f;

        if ((IsZero(s.OffsetLow) && IsZero(s.OffsetHigh)) || (s.OffsetPitchRange.X <= -90f && s.OffsetPitchRange.Y >= 90f))
        {
            return weaponModel;
        }

        Vector3 offset;
        if (pitch <= s.OffsetPitchRange.X && !IsZero(s.OffsetLow))
        {
            offset = Vector3.Lerp(s.OffsetLow, Vector3.Zero, MathUtils.Saturate((pitch + 90f) / (s.OffsetPitchRange.X + 90f)));
        }
        else if (pitch >= s.OffsetPitchRange.Y && !IsZero(s.OffsetHigh))
        {
            offset = s.OffsetHigh * MathUtils.Saturate((pitch - s.OffsetPitchRange.Y) / (90f - s.OffsetPitchRange.Y));
        }
        else
        {
            return weaponModel;
        }

        offset = new Vector3(
            MathF.Abs(offset.X) <= 0.001f ? 0f : offset.X,
            MathF.Abs(offset.Y) <= 0.001f ? 0f : offset.Y,
            MathF.Abs(offset.Z) <= 0.001f ? 0f : offset.Z);

        var local = Concat(pivot.Inverse(), weaponModel);
        local.Position += TransformMath.RotateVector(local.Angle, offset);
        return Concat(pivot, local);
    }

    // Aims the barrel (end to tip) along the aim direction, less during actions
    Transform AlignToAim(Transform weaponModel, Vector3 aimDirection, float actionWeight)
    {
        var end = Concat(weaponModel, Local(weaponEnd));
        var tip = Concat(weaponModel, Local(weaponTip));
        var endToWeapon = Concat(end.Inverse(), weaponModel);

        var arc = ValveMath.FromTo(TransformMath.NormalizeOrZero(tip.Position - end.Position), aimDirection);
        var rotation = TransformMath.FastSLerp(arc, Quaternion.Identity, actionWeight);
        end.Angle = rotation * end.Angle;
        return Concat(end, endToWeapon);
    }

    void HeadLookAtTip(Transform tip, float t)
    {
        var neckModel = pose.GetModelSpaceTransform(neck0);
        var headModel = Concat(neckModel, Local(head0));

        // The head's local +Y is model forward in the reference pose
        var direction = TransformMath.NormalizeOrZero(tip.Position - headModel.Position);
        var target = ValveMath.FromTo(TransformMath.RotateVector(headModel.Angle, Vector3.UnitY), direction) * headModel.Angle;

        var lookAt = new Transform(headModel.Position, headModel.Scale, TransformMath.FastSLerp(headModel.Angle, target, t));
        pose.SetTransform(head0, Concat(neckModel.Inverse(), lookAt));
    }
}
