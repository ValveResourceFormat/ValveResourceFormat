using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // Minimal port of Esoterica's bone mask task list: holds either a skeleton mask reference or a
    // uniform weight. Task chaining (blending two lists) degrades to picking the dominant list.
    struct BoneMaskTaskList
    {
        public static BoneMaskTaskList Default { get; }

        // Skeleton mask index + 1, so default(BoneMaskTaskList) means "no mask".
        private short maskIndexPlusOne;
        private float uniformWeight;
        private bool hasUniformWeight;

        public readonly bool IsSet => maskIndexPlusOne != 0 || hasUniformWeight;

        public void EmplaceTask(byte maskIndex)
        {
            maskIndexPlusOne = (short)(maskIndex + 1);
            hasUniformWeight = false;
        }

        public void EmplaceTask(float uniformWeight)
        {
            this.uniformWeight = uniformWeight;
            hasUniformWeight = true;
            maskIndexPlusOne = 0;
        }

        public void CopyFrom(BoneMaskTaskList other)
        {
            this = other;
        }

        public void SetToBlendBetweenTaskLists(BoneMaskTaskList listA, BoneMaskTaskList listB, float t)
        {
            // Coarse approximation: pick the dominant list instead of blending mask weights.
            this = t < 0.5f ? listA : listB;
        }

        /// <summary>Gets the blend weight for one bone, resolving skeleton masks by bone ID.</summary>
        public readonly float GetBoneWeight(Skeleton? skeleton, int boneIdx)
        {
            if (maskIndexPlusOne != 0 && skeleton != null)
            {
                var weights = skeleton.GetResolvedMaskWeights(maskIndexPlusOne - 1);
                return boneIdx < weights.Length ? weights[boneIdx] : 0f;
            }

            return hasUniformWeight ? uniformWeight : 1f;
        }
    }

    partial class BoneMaskValueNode
    {
        protected BoneMaskTaskList TaskList;
        BoneMaskTaskList cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public BoneMaskTaskList GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual BoneMaskTaskList GetValueInternal(GraphContext ctx) => TaskList;
    }

    partial class BoneMaskNode
    {
        public override void Initialize(GraphContext ctx)
        {
            // ctx.Skeleton (AnimLib.Skeleton, holding the mask definitions) is not wired up yet, so it can
            // be null — fall back to a uniform-weight mask rather than crashing at graph load.
            var maskIndex = ctx.Skeleton?.GetBoneMaskIndex(BoneMaskID) ?? -1;
            if (maskIndex != -1)
            {
                Debug.Assert(maskIndex >= 0 && maskIndex < 255);
                TaskList.EmplaceTask((byte)maskIndex);
            }
            else
            {
                ctx.LogWarning(NodeIdx, $"Couldn't find bone mask with ID: {BoneMaskID}");
                TaskList.EmplaceTask(0.0f);
            }
        }
    }

    partial class FixedWeightBoneMaskNode
    {
        public override void Initialize(GraphContext ctx)
        {
            TaskList.EmplaceTask(BoneWeight);
        }
    }

    partial class BoneMaskBlendNode
    {
        BoneMaskValueNode SourceBoneMask;
        BoneMaskValueNode TargetBoneMask;
        FloatValueNode BlendWeightValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceMaskNodeIdx, ref SourceBoneMask);
            ctx.SetNodeFromIndex(TargetMaskNodeIdx, ref TargetBoneMask);
            ctx.SetNodeFromIndex(BlendWeightValueNodeIdx, ref BlendWeightValueNode);
        }

        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx)
        {
            var sourceTaskList = SourceBoneMask.GetValue(ctx);
            var targetTaskList = TargetBoneMask.GetValue(ctx);
            var blendWeight = BlendWeightValueNode.GetValue(ctx);

            if (blendWeight <= 0.0f)
            {
                TaskList = sourceTaskList;
            }
            else if (blendWeight >= 1.0f)
            {
                TaskList = targetTaskList;
            }
            else
            {
                // Perform the blend into this node's task list. keep the original call commented so you can add it.
                TaskList = sourceTaskList;
                // TaskList.SetToBlendBetweenTaskLists(sourceTaskList, targetTaskList, blendWeight);
            }

            return TaskList;
        }
    }

    partial class BoneMaskSelectorNode
    {
        IDValueNode ParameterValueNode;
        BoneMaskValueNode? DefaultMaskValueNode;
        BoneMaskValueNode[] MaskOptions;
        int SelectedMaskIndex;
        int NewMaskIndex;
        float CurrentTimeInBlend;
        bool Blending;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ParameterValueNodeIdx, ref ParameterValueNode);
            ctx.SetOptionalNodeFromIndex(DefaultMaskNodeIdx, ref DefaultMaskValueNode);
            ctx.SetNodesFromIndexArray(MaskNodeIndices, ref MaskOptions);

            SelectedMaskIndex = TrySelectMask(ctx);
            NewMaskIndex = -1;
            Blending = false;
        }

        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx)
        {
            // Perform selection
            //-------------------------------------------------------------------------
            if (SwitchDynamically)
            {
                // Only try to select a new mask if we are not blending
                if (!Blending)
                {
                    NewMaskIndex = TrySelectMask(ctx);

                    // If the new mask is the same as the current one, do nothing
                    if (NewMaskIndex == SelectedMaskIndex)
                    {
                        NewMaskIndex = -1;
                    }
                    else // Start a blend to the new mask
                    {
                        CurrentTimeInBlend = 0f;
                        Blending = true;
                    }
                }
            }

            // Generate task list
            //-------------------------------------------------------------------------

            if (Blending)
            {
                CurrentTimeInBlend += ctx.DeltaTime;
                var blendWeight = CurrentTimeInBlend / BlendTimeSeconds;

                // If the blend is complete, then update the selected mask index
                if (blendWeight >= 1.0f)
                {
                    TaskList.CopyFrom(GetBoneMaskForIndex(ctx, NewMaskIndex));
                    SelectedMaskIndex = NewMaskIndex;
                    NewMaskIndex = -1;
                    Blending = false;
                }
                else // Perform blend and return the result
                {
                    TaskList.SetToBlendBetweenTaskLists(GetBoneMaskForIndex(ctx, SelectedMaskIndex), GetBoneMaskForIndex(ctx, NewMaskIndex), blendWeight);
                }
            }
            else
            {
                TaskList.CopyFrom(GetBoneMaskForIndex(ctx, SelectedMaskIndex));
            }

            return TaskList;
        }

        private int TrySelectMask(GraphContext ctx) => ParameterValues.IndexOf(ParameterValueNode.GetValue(ctx));

        private BoneMaskTaskList GetBoneMaskForIndex(GraphContext ctx, int optionIndex)
        {
            Debug.Assert(optionIndex >= -1 && optionIndex < MaskOptions.Length);

            if (optionIndex != -1)
            {
                return MaskOptions[optionIndex].GetValue(ctx);
            }

            if (DefaultMaskValueNode != null)
            {
                return DefaultMaskValueNode.GetValue(ctx);
            }

            return BoneMaskTaskList.Default;
        }
    }

    partial class BoneMaskSwitchNode
    {
        BoolValueNode SwitchValueNode;
        BoneMaskValueNode TrueValueNode;
        BoneMaskValueNode FalseValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SwitchValueNodeIdx, ref SwitchValueNode);
            ctx.SetNodeFromIndex(TrueValueNodeIdx, ref TrueValueNode);
            ctx.SetNodeFromIndex(FalseValueNodeIdx, ref FalseValueNode);
        }

        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx)
        {
            var switchValue = SwitchValueNode.GetValue(ctx);

            // SwitchDynamically
            // BlendTimeSeconds
            // todo: this is supposed to blend like BoneMaskSelectorNode

            return switchValue ? TrueValueNode.GetValue(ctx) : FalseValueNode.GetValue(ctx);
        }
    }

    partial class VirtualParameterBoneMaskNode
    {
        BoneMaskValueNode ChildNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the BoneMaskValueNode base.
        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
