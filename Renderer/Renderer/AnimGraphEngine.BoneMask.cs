using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Renderer.AnimLib
{
    // A bone mask operation (Esoterica BoneMaskTask)
    struct BoneMaskTask
    {
        public enum TaskType : byte
        {
            Mask = 0,
            GenerateMask,
            Blend,
            Scale,
            Combine,
        }

        public float Weight;
        public sbyte SourceTaskIdx;
        public sbyte TargetTaskIdx;
        public byte MaskSetIdx;
        public TaskType Type;

        /// <summary>Creates a mask generate task (all weights set to <paramref name="weight"/>).</summary>
        public static BoneMaskTask Generate(float weight)
            => new() { Type = TaskType.GenerateMask, Weight = weight, SourceTaskIdx = -1, TargetTaskIdx = -1, MaskSetIdx = 0xFF };

        /// <summary>Creates a skeleton bone mask reference task.</summary>
        public static BoneMaskTask Mask(byte maskSetIdx)
            => new() { Type = TaskType.Mask, SourceTaskIdx = -1, TargetTaskIdx = -1, MaskSetIdx = maskSetIdx };

        /// <summary>Creates a scale task.</summary>
        public static BoneMaskTask Scale(sbyte sourceIdx, float scale)
            => new() { Type = TaskType.Scale, SourceTaskIdx = sourceIdx, TargetTaskIdx = -1, Weight = scale, MaskSetIdx = 0xFF };

        /// <summary>Creates a combine (multiply) task.</summary>
        public static BoneMaskTask Combine(sbyte sourceIdx, sbyte targetIdx)
            => new() { Type = TaskType.Combine, SourceTaskIdx = sourceIdx, TargetTaskIdx = targetIdx, Weight = -1f, MaskSetIdx = 0xFF };

        /// <summary>Creates a blend task (lerp from source result to target result).</summary>
        public static BoneMaskTask Blend(sbyte sourceIdx, sbyte targetIdx, float blendWeight)
            => new() { Type = TaskType.Blend, SourceTaskIdx = sourceIdx, TargetTaskIdx = targetIdx, Weight = blendWeight, MaskSetIdx = 0xFF };
    }

    [InlineArray(BoneMaskTaskList.MaxTasks)]
    struct BoneMaskTaskBuffer
    {
        private BoneMaskTask element0;
    }

    // A command list of bone mask operations (Esoterica BoneMaskTaskList). Value type: assignment
    // copies the whole list, matching the C++ inline-vector semantics.
    struct BoneMaskTaskList
    {
        public const int MaxTasks = 32;

        // A default mask list with a single generate task (all weights = 1.0f), C++ s_defaultMaskList
        public static BoneMaskTaskList Default
        {
            get
            {
                var list = new BoneMaskTaskList();
                list.EmplaceTask(1.0f);
                return list;
            }
        }

        private BoneMaskTaskBuffer tasks;
        private int count;

        public readonly bool HasTasks => count > 0;

        public void Reset() => count = 0;

        public readonly sbyte LastTaskIdx => (sbyte)(count - 1);

        public sbyte CopyFrom(in BoneMaskTaskList other)
        {
            this = other;
            return LastTaskIdx;
        }

        private void Add(in BoneMaskTask task)
        {
            Debug.Assert(count < MaxTasks);
            if (count < MaxTasks)
            {
                tasks[count++] = task;
            }
        }

        /// <summary>Emplaces a skeleton bone mask reference task.</summary>
        public void EmplaceTask(byte maskIndex) => Add(BoneMaskTask.Mask(maskIndex));

        /// <summary>Emplaces a mask generate task with a uniform weight.</summary>
        public void EmplaceTask(float uniformWeight) => Add(BoneMaskTask.Generate(uniformWeight));

        /// <summary>Blends this task list's result to the result of the supplied list.</summary>
        public sbyte BlendTo(in BoneMaskTaskList taskList, float blendWeight)
        {
            var sourceTaskIdx = LastTaskIdx;
            var targetTaskIdx = AppendTaskListAndFixDependencies(taskList);
            Add(BoneMaskTask.Blend(sourceTaskIdx, targetTaskIdx, blendWeight));
            return LastTaskIdx;
        }

        /// <summary>Combines (multiplies) the result of this task list with the result of the supplied list.</summary>
        public sbyte CombineWith(in BoneMaskTaskList taskList)
        {
            var sourceTaskIdx = LastTaskIdx;
            var targetTaskIdx = AppendTaskListAndFixDependencies(taskList);
            Add(BoneMaskTask.Combine(sourceTaskIdx, targetTaskIdx));
            return LastTaskIdx;
        }

        /// <summary>Sets this task list to a blend between two task lists.</summary>
        public sbyte SetToBlendBetweenTaskLists(in BoneMaskTaskList sourceTaskList, in BoneMaskTaskList targetTaskList, float blendWeight)
        {
            if (blendWeight == 0f)
            {
                this = sourceTaskList;
            }
            else if (blendWeight == 1f)
            {
                this = targetTaskList;
            }
            else
            {
                this = sourceTaskList;
                BlendTo(targetTaskList, blendWeight);
            }

            return LastTaskIdx;
        }

        /// <summary>Creates a blend from the current registered tasks to a generated mask.</summary>
        public sbyte BlendToGeneratedMask(float generatedMaskWeight, float blendWeight)
        {
            Debug.Assert(blendWeight >= 0f && blendWeight <= 1f);

            if (blendWeight == 0f)
            {
                // Do nothing
            }
            else if (blendWeight == 1f)
            {
                count = 0;
                Add(BoneMaskTask.Generate(generatedMaskWeight));
            }
            else
            {
                var sourceTaskIdx = LastTaskIdx;
                Add(BoneMaskTask.Generate(generatedMaskWeight));
                var targetTaskIdx = LastTaskIdx;
                Add(BoneMaskTask.Blend(sourceTaskIdx, targetTaskIdx, blendWeight));
            }

            return LastTaskIdx;
        }

        /// <summary>Creates a blend from a generated mask to the current registered tasks.</summary>
        public sbyte BlendFromGeneratedMask(float generatedMaskWeight, float blendWeight)
            => BlendToGeneratedMask(generatedMaskWeight, 1f - blendWeight);

        private sbyte AppendTaskListAndFixDependencies(in BoneMaskTaskList taskList)
        {
            var dependencyOffset = (sbyte)(LastTaskIdx + 1);

            for (var i = 0; i < taskList.count; i++)
            {
                var task = taskList.tasks[i];
                if (task.Type is BoneMaskTask.TaskType.Blend or BoneMaskTask.TaskType.Combine)
                {
                    task.SourceTaskIdx += dependencyOffset;
                    task.TargetTaskIdx += dependencyOffset;
                }

                Add(task);
            }

            return LastTaskIdx;
        }

        /// <summary>
        /// Executes the task list, writing the resulting per-bone weight into
        /// <paramref name="result"/> (Esoterica BoneMaskTaskList::GenerateBoneMask).
        /// </summary>
        public readonly void GenerateBoneMask(Skeleton? skeleton, BoneMaskPool pool, float[] result)
        {
            Debug.Assert(HasTasks);

            Span<int> maskBufferIndices = stackalloc int[count];

            for (var i = 0; i < count; i++)
            {
                ref readonly var task = ref tasks[i];

                switch (task.Type)
                {
                    case BoneMaskTask.TaskType.Mask:
                    {
                        maskBufferIndices[i] = pool.Acquire(result.Length);
                        var buffer = pool.GetBuffer(maskBufferIndices[i]);
                        var maskWeights = skeleton?.GetResolvedMaskWeights(task.MaskSetIdx);
                        if (maskWeights != null)
                        {
                            var n = Math.Min(maskWeights.Length, buffer.Length);
                            maskWeights.AsSpan(0, n).CopyTo(buffer);
                            buffer.AsSpan(n).Clear();
                        }
                        else
                        {
                            Array.Fill(buffer, 1f);
                        }

                        break;
                    }

                    case BoneMaskTask.TaskType.GenerateMask:
                    {
                        maskBufferIndices[i] = pool.Acquire(result.Length);
                        Array.Fill(pool.GetBuffer(maskBufferIndices[i]), task.Weight);
                        break;
                    }

                    case BoneMaskTask.TaskType.Scale:
                    {
                        var sourceBuffer = pool.GetBuffer(maskBufferIndices[task.SourceTaskIdx]);
                        maskBufferIndices[i] = maskBufferIndices[task.SourceTaskIdx];
                        for (var b = 0; b < sourceBuffer.Length; b++)
                        {
                            sourceBuffer[b] *= task.Weight;
                        }

                        break;
                    }

                    case BoneMaskTask.TaskType.Combine:
                    case BoneMaskTask.TaskType.Blend:
                    {
                        var sourceBuffer = pool.GetBuffer(maskBufferIndices[task.SourceTaskIdx]);
                        var targetBuffer = pool.GetBuffer(maskBufferIndices[task.TargetTaskIdx]);
                        maskBufferIndices[i] = maskBufferIndices[task.TargetTaskIdx];

                        if (task.Type == BoneMaskTask.TaskType.Combine)
                        {
                            for (var b = 0; b < targetBuffer.Length; b++)
                            {
                                targetBuffer[b] *= sourceBuffer[b];
                            }
                        }
                        else // Blend: lerp from the source result towards the target result
                        {
                            for (var b = 0; b < targetBuffer.Length; b++)
                            {
                                targetBuffer[b] = MathUtils.Lerp(sourceBuffer[b], targetBuffer[b], task.Weight);
                            }
                        }

                        break;
                    }
                }
            }

            pool.GetBuffer(maskBufferIndices[count - 1]).AsSpan(0, result.Length).CopyTo(result);
            pool.ReleaseAll();
        }
    }

    /// <summary>
    /// Reusable per-bone weight buffers for bone mask task list evaluation. Buffers persist for the
    /// lifetime of the graph and are recycled via <see cref="ReleaseAll"/> after each evaluation.
    /// </summary>
    class BoneMaskPool
    {
        private readonly List<float[]> buffers = [];
        private int firstFree;

        public int Acquire(int size)
        {
            if (firstFree == buffers.Count)
            {
                buffers.Add(new float[size]);
            }
            else if (buffers[firstFree].Length < size)
            {
                buffers[firstFree] = new float[size];
            }

            return firstFree++;
        }

        public float[] GetBuffer(int index) => buffers[index];

        public void ReleaseAll() => firstFree = 0;
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
        public override void Instantiate(GraphContext ctx)
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
        public override void Instantiate(GraphContext ctx)
        {
            TaskList.EmplaceTask(BoneWeight);
        }
    }

    partial class BoneMaskBlendNode
    {
        BoneMaskValueNode SourceBoneMask;
        BoneMaskValueNode TargetBoneMask;
        FloatValueNode BlendWeightValueNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceMaskNodeIdx, ref SourceBoneMask);
            ctx.SetNodeFromIndex(TargetMaskNodeIdx, ref TargetBoneMask);
            ctx.SetNodeFromIndex(BlendWeightValueNodeIdx, ref BlendWeightValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            SourceBoneMask.Initialize(ctx);
            TargetBoneMask.Initialize(ctx);
            BlendWeightValueNode.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            SourceBoneMask.Shutdown(ctx);
            TargetBoneMask.Shutdown(ctx);
            BlendWeightValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx)
        {
            var blendWeight = BlendWeightValueNode.GetValue(ctx);

            // If we dont need to perform the blend, set the value to the required source
            if (blendWeight <= 0.0f)
            {
                TaskList = SourceBoneMask.GetValue(ctx);
            }
            else if (blendWeight >= 1.0f)
            {
                TaskList = TargetBoneMask.GetValue(ctx);
            }
            else // Actually perform the blend
            {
                TaskList.SetToBlendBetweenTaskLists(SourceBoneMask.GetValue(ctx), TargetBoneMask.GetValue(ctx), blendWeight);
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

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ParameterValueNodeIdx, ref ParameterValueNode);
            ctx.SetOptionalNodeFromIndex(DefaultMaskNodeIdx, ref DefaultMaskValueNode);
            ctx.SetNodesFromIndexArray(MaskNodeIndices, ref MaskOptions);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            ParameterValueNode.Initialize(ctx);
            DefaultMaskValueNode?.Initialize(ctx);

            foreach (var option in MaskOptions)
            {
                option.Initialize(ctx);
            }

            SelectedMaskIndex = TrySelectMask(ctx);
            NewMaskIndex = -1;
            Blending = false;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            SelectedMaskIndex = -1;
            NewMaskIndex = -1;
            Blending = false;

            foreach (var option in MaskOptions)
            {
                option.Shutdown(ctx);
            }

            DefaultMaskValueNode?.Shutdown(ctx);
            ParameterValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
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
                    else if (BlendTimeSeconds > 0f) // Start a blend to the new mask
                    {
                        CurrentTimeInBlend = 0f;
                        Blending = true;
                    }
                    else // Immediately switch mask
                    {
                        SelectedMaskIndex = NewMaskIndex;
                        NewMaskIndex = -1;
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
        int SelectedMaskIndex;
        float CurrentTimeInBlend;
        bool Blending;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SwitchValueNodeIdx, ref SwitchValueNode);
            ctx.SetNodeFromIndex(TrueValueNodeIdx, ref TrueValueNode);
            ctx.SetNodeFromIndex(FalseValueNodeIdx, ref FalseValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);

            SwitchValueNode.Initialize(ctx);
            TrueValueNode.Initialize(ctx);
            FalseValueNode.Initialize(ctx);

            SelectedMaskIndex = SwitchValueNode.GetValue(ctx) ? 1 : 0;
            Blending = false;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            Blending = false;
            FalseValueNode.Shutdown(ctx);
            TrueValueNode.Shutdown(ctx);
            SwitchValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
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
                    var newMaskIdx = SwitchValueNode.GetValue(ctx) ? 1 : 0;

                    // If the new mask is the same as the current one, do nothing
                    if (newMaskIdx != SelectedMaskIndex)
                    {
                        SelectedMaskIndex = newMaskIdx;

                        if (BlendTimeSeconds > 0f)
                        {
                            CurrentTimeInBlend = 0f;
                            Blending = true;
                        }
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
                    TaskList.CopyFrom(GetBoneMaskForIndex(ctx, SelectedMaskIndex));
                    Blending = false;
                }
                else // Perform blend and return the result
                {
                    var sourceList = GetBoneMaskForIndex(ctx, SelectedMaskIndex == 1 ? 0 : 1);
                    var targetList = GetBoneMaskForIndex(ctx, SelectedMaskIndex == 1 ? 1 : 0);
                    TaskList.SetToBlendBetweenTaskLists(sourceList, targetList, blendWeight);
                }
            }
            else
            {
                TaskList.CopyFrom(GetBoneMaskForIndex(ctx, SelectedMaskIndex));
            }

            return TaskList;
        }

        private BoneMaskTaskList GetBoneMaskForIndex(GraphContext ctx, int index)
            => index == 1 ? TrueValueNode.GetValue(ctx) : FalseValueNode.GetValue(ctx);
    }

    partial class VirtualParameterBoneMaskNode
    {
        BoneMaskValueNode ChildNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            ChildNode.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ChildNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        // Caching is handled once-per-update by the BoneMaskValueNode base.
        protected override BoneMaskTaskList GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
