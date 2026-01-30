using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    struct BoneMaskTaskList
    {
        public static BoneMaskTaskList Default { get; }

        public void EmplaceTask(byte maskIndex)
        {
        }

        public void EmplaceTask(float uniformWeight)
        {

        }

        public void CopyFrom(BoneMaskTaskList other)
        {
        }

        public void SetToBlendBetweenTaskLists(BoneMaskTaskList listA, BoneMaskTaskList listB, float t)
        {
        }
    }

    partial class BoneMaskValueNode
    {
        protected BoneMaskTaskList TaskList;

        public virtual BoneMaskTaskList GetValue(GraphContext ctx)
        {
            return TaskList;
        }
    }

    partial class BoneMaskNode
    {
        public void Initialize(GraphContext ctx)
        {
            var maskIndex = ctx.Skeleton.GetBoneMaskIndex(BoneMaskID);
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
        public void Initialize(GraphContext ctx)
        {
            TaskList.EmplaceTask(BoneWeight);
        }
    }

    partial class BoneMaskBlendNode
    {
        BoneMaskValueNode SourceBoneMask;
        BoneMaskValueNode TargetBoneMask;
        FloatValueNode BlendWeightValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceMaskNodeIdx, ref SourceBoneMask);
            ctx.SetNodeFromIndex(TargetMaskNodeIdx, ref TargetBoneMask);
            ctx.SetNodeFromIndex(BlendWeightValueNodeIdx, ref BlendWeightValueNode);
        }

        public override BoneMaskTaskList GetValue(GraphContext ctx)
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

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ParameterValueNodeIdx, ref ParameterValueNode);
            ctx.SetOptionalNodeFromIndex(DefaultMaskNodeIdx, ref DefaultMaskValueNode);
            ctx.SetNodesFromIndexArray(MaskNodeIndices, ref MaskOptions);

            SelectedMaskIndex = TrySelectMask(ctx);
            NewMaskIndex = -1;
            Blending = false;
        }

        public override BoneMaskTaskList GetValue(GraphContext ctx)
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

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SwitchValueNodeIdx, ref SwitchValueNode);
            ctx.SetNodeFromIndex(TrueValueNodeIdx, ref TrueValueNode);
            ctx.SetNodeFromIndex(FalseValueNodeIdx, ref FalseValueNode);
        }

        public override BoneMaskTaskList GetValue(GraphContext ctx)
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
    }
}
