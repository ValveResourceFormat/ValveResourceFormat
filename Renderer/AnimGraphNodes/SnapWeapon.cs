using System.Diagnostics;
using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimGraphNodes;

// Snaps the weapon so its right hand attachment sits on the right hand, then reaches the left hand
// onto the weapon's left hand attachment with two bone IK.
sealed class SnapWeaponNode : PassthroughNode
{
    public short FlashedAmountNodeIdx { get; }
    public short WeaponCategoryNodeIdx { get; }
    public short WeaponTypeNodeIdx { get; }

    public SnapWeaponNode(KVObject data) : base(data)
    {
        FlashedAmountNodeIdx = data.GetInt16Property("m_nFlashedAmountNodeIdx");
        WeaponCategoryNodeIdx = data.GetInt16Property("m_nWeaponCategoryNodeIdx");
        WeaponTypeNodeIdx = data.GetInt16Property("m_nWeaponTypeNodeIdx");
    }

    static readonly GlobalSymbol DisableLeftHandIK = new("WPN_DISABLE_LEFT_HAND_IK");
    static readonly GlobalSymbol DisableHandIK = new("WPN_DISABLE_HAND_IK");
    static readonly GlobalSymbol CategoryGrenade = new("weapon_category_grenade");
    static readonly GlobalSymbol CategoryKnife = new("weapon_category_knife");
    static readonly GlobalSymbol TypeHealthshot = new("weapon_healthshot");
    static readonly GlobalSymbol TypeKnifePush = new("weapon_knife_push");

    FloatValueNode? FlashedAmountNode;
    IDValueNode? WeaponCategoryNode;
    IDValueNode? WeaponTypeNode;

    bool hasBones;
    int weaponHandRightIdx = -1;
    int weaponHandLeftIdx = -1;
    int handRightIdx = -1;
    int handLeftIdx = -1;
    int weaponIdx = -1;
    Pose solvePose;

    public override void Instantiate(GraphContext ctx)
    {
        base.Instantiate(ctx);
        ctx.SetOptionalNodeFromIndex(FlashedAmountNodeIdx, ref FlashedAmountNode);
        ctx.SetOptionalNodeFromIndex(WeaponCategoryNodeIdx, ref WeaponCategoryNode);
        ctx.SetOptionalNodeFromIndex(WeaponTypeNodeIdx, ref WeaponTypeNode);

        solvePose = new Pose(ctx.Skeleton);

        var skeleton = ctx.Skeleton;
        weaponHandRightIdx = skeleton.GetBoneIndex(new GlobalSymbol("wpnHand_R"));
        weaponHandLeftIdx = skeleton.GetBoneIndex(new GlobalSymbol("wpnHand_L"));
        handRightIdx = skeleton.GetBoneIndex(new GlobalSymbol("hand_R"));
        handLeftIdx = skeleton.GetBoneIndex(new GlobalSymbol("hand_L"));
        weaponIdx = skeleton.GetBoneIndex(new GlobalSymbol("wpn"));

        hasBones = weaponHandRightIdx != -1 && weaponHandLeftIdx != -1 && handRightIdx != -1 && handLeftIdx != -1 && weaponIdx != -1;
    }

    public override bool IsValid => (ChildNode?.IsValid ?? false) && hasBones;

    protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
    {
        base.InitializeInternal(ctx, initialTime);
        FlashedAmountNode?.Initialize(ctx);
        WeaponCategoryNode?.Initialize(ctx);
        WeaponTypeNode?.Initialize(ctx);

        if (!hasBones)
        {
            ctx.LogWarning(NodeIdx, "Weapon constraint node is being used with a skeleton that is missing the required bones!");
        }
    }

    protected override void ShutdownInternal(GraphContext ctx)
    {
        WeaponTypeNode?.Shutdown(ctx);
        WeaponCategoryNode?.Shutdown(ctx);
        FlashedAmountNode?.Shutdown(ctx);
        base.ShutdownInternal(ctx);
    }

    public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
    {
        var result = base.Update(ctx, updateRange);

        // Being flashed at all disables the left hand
        var leftHandEnabled = !(FlashedAmountNode != null && FlashedAmountNode.GetValue(ctx) > 0f);

        if (leftHandEnabled)
        {
            // The whole buffer is searched, regardless of weight or branch
            for (var i = 0; i < ctx.SampledEvents.Count; i++)
            {
                var id = ctx.SampledEvents[i].ID;
                if (id == DisableLeftHandIK || id == DisableHandIK)
                {
                    leftHandEnabled = false;
                    break;
                }
            }
        }

        var weaponType = WeaponTypeNode?.GetValue(ctx) ?? default;
        var weaponCategory = WeaponCategoryNode?.GetValue(ctx) ?? default;

        if (leftHandEnabled)
        {
            if (weaponCategory == CategoryGrenade || weaponType == TypeHealthshot || (weaponCategory == CategoryKnife && weaponType != TypeKnifePush))
            {
                leftHandEnabled = false;
            }
        }

        if (!IsValid)
        {
            return result;
        }

        solvePose.SetParentSpaceTransforms(result.Pose);
        if (SnapWeapon(ctx, leftHandEnabled))
        {
            solvePose.CopyParentSpaceTransformsTo(PoseTransforms);
            result.Pose = PoseTransforms;
        }

        return result;
    }

    static bool IsAncestor(Skeleton skeleton, int ancestorIdx, int boneIdx)
    {
        for (var b = skeleton.GetParentBoneIndex(boneIdx); b != -1; b = skeleton.GetParentBoneIndex(b))
        {
            if (b == ancestorIdx)
            {
                return true;
            }
        }

        return false;
    }

    // The molotov lighter and knife push modes drive a secondary weapon skeleton the graph does not
    // evaluate, so they only snap the weapon
    bool SnapWeapon(GraphContext ctx, bool leftHandEnabled)
    {
        var skeleton = ctx.Skeleton;
        var weaponParentIdx = skeleton.GetParentBoneIndex(weaponIdx);
        if (!IsAncestor(skeleton, weaponIdx, weaponHandRightIdx) || weaponParentIdx == -1)
        {
            return false;
        }

        var weaponModel = solvePose.GetModelSpaceTransform(weaponIdx);
        var weaponParentModel = solvePose.GetModelSpaceTransform(weaponParentIdx);
        var weaponHandRightModel = solvePose.GetModelSpaceTransform(weaponHandRightIdx);
        var handRightModel = solvePose.GetModelSpaceTransform(handRightIdx);

        // The weapon relative to its right hand attachment, moved onto the right hand
        var weaponFromHand = weaponModel * weaponHandRightModel.Inverse();
        var snappedWeapon = weaponFromHand * handRightModel;
        solvePose.SetTransform(weaponIdx, snappedWeapon * weaponParentModel.Inverse());

        if (leftHandEnabled)
        {
            // The left hand attachment is a direct child of the weapon
            Debug.Assert(skeleton.GetParentBoneIndex(weaponHandLeftIdx) == weaponIdx);
            var target = solvePose.GetTransform(weaponHandLeftIdx) * snappedWeapon;
            TwoBoneSolver.Solve(solvePose, handLeftIdx, target);
        }

        return true;
    }
}
