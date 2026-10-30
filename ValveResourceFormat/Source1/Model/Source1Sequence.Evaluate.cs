using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class Source1Sequence
{
    private const int MaxLayerDepth = 8;
    private const float BlendEdge = 0.001f;
    private const float MaxCycle = 0.999f;
    private const int MaxStackBones = 256;

    /// <summary>
    /// Evaluates the sequence at a cycle to bone-local transforms, starting from the reference pose of the model.
    /// The pose of a sequence is blended over the reference pose per bone by <see cref="BoneWeights"/>, and the
    /// autolayers of the sequence are applied on top. A delta sequence is added to the reference pose.
    /// </summary>
    /// <param name="cycle">Position in the sequence from 0 to 1, wrapped for looping sequences and clamped otherwise.</param>
    /// <param name="poseParameterValues">
    /// Values of the pose parameters of the model in their own units, indexed like
    /// <see cref="Source1Model.PoseParameters"/>. Missing values are zero.
    /// </param>
    /// <param name="positions">Receives local positions, at least as many as the model has bones.</param>
    /// <param name="rotations">Receives local rotations, at least as many as the model has bones.</param>
    public void Evaluate(float cycle, ReadOnlySpan<float> poseParameterValues, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var model = RequireModel(positions, rotations);

        model.GetReferencePose(positions, rotations);
        AccumulateCore(model, cycle, poseParameterValues, 1f, positions, rotations, 0);
    }

    /// <summary>
    /// Evaluates a delta sequence on its own, as offsets from a zero position and identity rotation
    /// instead of from the reference pose.
    /// </summary>
    /// <inheritdoc cref="Evaluate"/>
    public void EvaluateAdditive(float cycle, ReadOnlySpan<float> poseParameterValues, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var model = RequireModel(positions, rotations);

        positions[..model.Bones.Length].Clear();
        rotations[..model.Bones.Length].Fill(Quaternion.Identity);
        AccumulateCore(model, cycle, poseParameterValues, 1f, positions, rotations, 0);
    }

    /// <summary>
    /// Applies the sequence on top of an existing pose. An absolute sequence is blended in per bone, a delta
    /// sequence is added to the pose. Autolayers of the sequence are applied as well.
    /// </summary>
    /// <param name="cycle">Position in the sequence from 0 to 1.</param>
    /// <param name="poseParameterValues">Values of the pose parameters, see <see cref="Evaluate"/>.</param>
    /// <param name="weight">Weight from 0 to 1.</param>
    /// <param name="positions">Local positions of the pose to modify.</param>
    /// <param name="rotations">Local rotations of the pose to modify.</param>
    public void Accumulate(float cycle, ReadOnlySpan<float> poseParameterValues, float weight, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var model = RequireModel(positions, rotations);

        AccumulateCore(model, cycle, poseParameterValues, weight, positions, rotations, 0);
    }

    /// <summary>Cycles per second of the sequence, which blends the frame rates of the animations at the pose parameters.</summary>
    /// <param name="poseParameterValues">Values of the pose parameters, see <see cref="Evaluate"/>.</param>
    public float GetCyclesPerSecond(ReadOnlySpan<float> poseParameterValues)
    {
        var model = Model ?? throw new InvalidOperationException("The sequence was not loaded from a model.");

        GetLocalPoseParameter(model, 0, poseParameterValues, out var s0, out var i0);
        GetLocalPoseParameter(model, 1, poseParameterValues, out var s1, out var i1);

        var total = 0f;
        total += GetCyclesPerSecond(GetBlend(i0, i1), (1f - s0) * (1f - s1));
        total += GetCyclesPerSecond(GetBlend(i0 + 1, i1), s0 * (1f - s1));
        total += GetCyclesPerSecond(GetBlend(i0, i1 + 1), (1f - s0) * s1);
        total += GetCyclesPerSecond(GetBlend(i0 + 1, i1 + 1), s0 * s1);
        return total;
    }

    /// <summary>Length of one cycle in seconds, zero for a sequence without motion.</summary>
    /// <param name="poseParameterValues">Values of the pose parameters, see <see cref="Evaluate"/>.</param>
    public float GetDuration(ReadOnlySpan<float> poseParameterValues)
    {
        var cyclesPerSecond = GetCyclesPerSecond(poseParameterValues);
        return cyclesPerSecond > 0f ? 1f / cyclesPerSecond : 0f;
    }

    private static float GetCyclesPerSecond(Source1Animation animation, float weight)
    {
        if (weight <= 0f || animation.FrameCount <= 1)
        {
            return 0f;
        }

        return animation.Fps / (animation.FrameCount - 1) * weight;
    }

    private Source1Model RequireModel(Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var model = Model ?? throw new InvalidOperationException("The sequence was not loaded from a model.");

        if (positions.Length < model.Bones.Length || rotations.Length < model.Bones.Length)
        {
            throw new ArgumentException("Output spans are shorter than the bone count.");
        }

        return model;
    }

    private Source1Animation GetBlend(int x, int y)
    {
        var column = Math.Clamp(x, 0, BlendCountX - 1);
        var row = Math.Clamp(y, 0, BlendCountY - 1);
        return Blends[(row * BlendCountX) + column];
    }

    private static float GetPoseValue(Source1Model model, ReadOnlySpan<float> values, int index)
    {
        var parameter = model.PoseParameters[index];
        var value = index < values.Length ? values[index] : 0f;

        if (!float.IsFinite(value))
        {
            value = 0f;
        }

        if (parameter.Loop != 0f)
        {
            var wrap = ((parameter.Start + parameter.End) / 2f) + (parameter.Loop / 2f);
            var shift = parameter.Loop - wrap;
            value -= parameter.Loop * MathF.Floor((value + shift) / parameter.Loop);
        }

        var low = MathF.Min(parameter.Start, parameter.End);
        var high = MathF.Max(parameter.Start, parameter.End);
        return Math.Clamp(value, low, high);
    }

    private static float GetNormalizedPoseValue(Source1Model model, ReadOnlySpan<float> values, int index)
    {
        var parameter = model.PoseParameters[index];
        var range = parameter.End - parameter.Start;

        if (range == 0f)
        {
            return 0f;
        }

        var value = GetPoseValue(model, values, index);
        return MathUtils.Saturate((value - parameter.Start) / range);
    }

    private void GetLocalPoseParameter(Source1Model model, int axis, ReadOnlySpan<float> values, out float setting, out int index)
    {
        setting = 0f;
        index = 0;

        var poseIndex = axis == 0 ? BlendIndexX : BlendIndexY;
        var count = axis == 0 ? BlendCountX : BlendCountY;

        if ((uint)poseIndex >= (uint)model.PoseParameters.Length)
        {
            return;
        }

        var parameter = model.PoseParameters[poseIndex];
        var range = parameter.End - parameter.Start;

        if (range == 0f)
        {
            return;
        }

        if (PoseKeys.Length == 0)
        {
            var start = axis == 0 ? BlendStartX : BlendStartY;
            var end = axis == 0 ? BlendEndX : BlendEndY;
            var localStart = (start - parameter.Start) / range;
            var localEnd = (end - parameter.Start) / range;
            var span = localEnd - localStart;

            if (MathF.Abs(span) < 1e-6f)
            {
                return;
            }

            var normalized = GetNormalizedPoseValue(model, values, poseIndex);
            setting = MathUtils.Saturate((normalized - localStart) / span);

            if (count > 2)
            {
                index = (int)(setting * (count - 1));

                if (index == count - 1)
                {
                    index = count - 2;
                }

                setting = (setting * (count - 1)) - index;
            }

            return;
        }

        if (count < 2)
        {
            return;
        }

        var value = GetPoseValue(model, values, poseIndex);
        var keyOffset = axis == 0 ? 0 : BlendCountX;

        while (true)
        {
            if (keyOffset + index + 1 >= PoseKeys.Length)
            {
                setting = 0f;
                return;
            }

            var low = PoseKeys[keyOffset + index];
            var high = PoseKeys[keyOffset + index + 1];

            setting = high == low ? 0f : (value - low) / (high - low);

            if (index < count - 2 && setting > 1f)
            {
                index++;
                continue;
            }

            break;
        }

        setting = MathUtils.Saturate(setting);
    }

    private float ResolveCycle(Source1Model model, float cycle, ReadOnlySpan<float> poseParameterValues)
    {
        if ((Flags & Source1AnimationFlags.Realtime) != 0)
        {
            return cycle;
        }

        if ((Flags & Source1AnimationFlags.CyclePose) != 0)
        {
            if ((uint)CyclePoseIndex < (uint)model.PoseParameters.Length)
            {
                return GetNormalizedPoseValue(model, poseParameterValues, CyclePoseIndex);
            }

            return 0f;
        }

        if (!float.IsFinite(cycle))
        {
            return 0f;
        }

        if (IsLooping)
        {
            return cycle - MathF.Floor(cycle);
        }

        // A sequence that does not loop never reaches its last frame
        return Math.Clamp(cycle, 0f, MaxCycle);
    }

    private static void SampleAnimation(Source1Animation animation, float cycle, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        animation.GetFrame(cycle * (animation.FrameCount - 1), positions, rotations);
    }

    private static void BlendPoses(Source1Model model, Span<Vector3> positions1, Span<Quaternion> rotations1, ReadOnlySpan<Vector3> positions2, ReadOnlySpan<Quaternion> rotations2, float s)
    {
        var s1 = 1f - s;

        for (var i = 0; i < model.Bones.Length; i++)
        {
            if ((model.Bones[i].Flags & Source1PoseMath.FixedAlignmentBoneFlag) != 0)
            {
                rotations1[i] = Source1PoseMath.BlendNoAlign(rotations2[i], rotations1[i], s1);
            }
            else
            {
                rotations1[i] = Source1PoseMath.Blend(rotations2[i], rotations1[i], s1);
            }

            positions1[i] = (positions1[i] * s1) + (positions2[i] * s);
        }
    }

    // Returns false when the pose is an all-zero animation at the lower edge of the second blend axis, which adds nothing to the pose
    private bool CalcPoseSingle(Source1Model model, float cycle, ReadOnlySpan<float> poseParameterValues, Span<Vector3> positions, Span<Quaternion> rotations)
    {
        var boneCount = model.Bones.Length;

        GetLocalPoseParameter(model, 0, poseParameterValues, out var s0, out var i0);
        GetLocalPoseParameter(model, 1, poseParameterValues, out var s1, out var i1);

        cycle = ResolveCycle(model, cycle, poseParameterValues);

        var xEdge = s0 < BlendEdge || s0 > 1f - BlendEdge;
        var yEdge = s1 < BlendEdge || s1 > 1f - BlendEdge;
        var x = s0 > 1f - BlendEdge ? i0 + 1 : i0;
        var y = s1 > 1f - BlendEdge ? i1 + 1 : i1;

        if (xEdge && yEdge)
        {
            var animation = GetBlend(x, y);

            if (s1 < BlendEdge && (animation.Flags & Source1AnimationFlags.AllZeros) != 0)
            {
                return false;
            }

            SampleAnimation(animation, cycle, positions, rotations);
            return true;
        }

        var positions2 = boneCount <= MaxStackBones ? stackalloc Vector3[boneCount] : new Vector3[boneCount];
        var rotations2 = boneCount <= MaxStackBones ? stackalloc Quaternion[boneCount] : new Quaternion[boneCount];

        if (xEdge)
        {
            SampleAnimation(GetBlend(x, i1), cycle, positions, rotations);
            SampleAnimation(GetBlend(x, i1 + 1), cycle, positions2, rotations2);
            BlendPoses(model, positions, rotations, positions2, rotations2, s1);
            return true;
        }

        if (yEdge)
        {
            SampleAnimation(GetBlend(i0, y), cycle, positions, rotations);
            SampleAnimation(GetBlend(i0 + 1, y), cycle, positions2, rotations2);
            BlendPoses(model, positions, rotations, positions2, rotations2, s0);
            return true;
        }

        Span<int> animationX = stackalloc int[3];
        Span<int> animationY = stackalloc int[3];
        Span<float> weight = stackalloc float[3];

        Calc3WayBlend(i0, i1, s0, s1, animationX, animationY, weight);

        if (weight[1] < BlendEdge)
        {
            SampleAnimation(GetBlend(animationX[0], animationY[0]), cycle, positions, rotations);
            SampleAnimation(GetBlend(animationX[2], animationY[2]), cycle, positions2, rotations2);
            BlendPoses(model, positions, rotations, positions2, rotations2, weight[2] / (weight[0] + weight[2]));
            return true;
        }

        SampleAnimation(GetBlend(animationX[0], animationY[0]), cycle, positions, rotations);
        SampleAnimation(GetBlend(animationX[1], animationY[1]), cycle, positions2, rotations2);
        BlendPoses(model, positions, rotations, positions2, rotations2, weight[1] / (weight[0] + weight[1]));

        SampleAnimation(GetBlend(animationX[2], animationY[2]), cycle, positions2, rotations2);
        BlendPoses(model, positions, rotations, positions2, rotations2, weight[2]);
        return true;
    }

    private static void Calc3WayBlend(int i0, int i1, float s0, float s1, Span<int> animationX, Span<int> animationY, Span<float> weight)
    {
        var isEven = ((i0 + i1) & 1) == 0;

        int x1, y1, x2, y2, x3, y3;

        if (isEven)
        {
            if (s0 > s1)
            {
                (x1, y1, x2, y2, x3, y3) = (0, 0, 1, 0, 1, 1);
                weight[0] = 1f - s0;
                weight[1] = s0 - s1;
            }
            else
            {
                (x1, y1, x2, y2, x3, y3) = (1, 1, 0, 1, 0, 0);
                weight[0] = s0;
                weight[1] = s1 - s0;
            }
        }
        else if (s0 + s1 > 1f)
        {
            (x1, y1, x2, y2, x3, y3) = (1, 0, 1, 1, 0, 1);
            weight[0] = 1f - s1;
            weight[1] = s0 - 1f + s1;
        }
        else
        {
            (x1, y1, x2, y2, x3, y3) = (0, 1, 0, 0, 1, 0);
            weight[0] = s1;
            weight[1] = 1f - s0 - s1;
        }

        animationX[0] = i0 + x1;
        animationY[0] = i1 + y1;
        animationX[1] = i0 + x2;
        animationY[1] = i1 + y2;
        animationX[2] = i0 + x3;
        animationY[2] = i1 + y3;

        if (weight[1] < BlendEdge)
        {
            weight[1] = 0f;
        }

        weight[2] = 1f - weight[0] - weight[1];
    }

    private void AccumulateCore(Source1Model model, float cycle, ReadOnlySpan<float> poseParameterValues, float weight, Span<Vector3> positions, Span<Quaternion> rotations, int depth)
    {
        if (depth > MaxLayerDepth)
        {
            return;
        }

        weight = MathUtils.Saturate(weight);

        var boneCount = model.Bones.Length;
        var positions2 = boneCount <= MaxStackBones ? stackalloc Vector3[boneCount] : new Vector3[boneCount];
        var rotations2 = boneCount <= MaxStackBones ? stackalloc Quaternion[boneCount] : new Quaternion[boneCount];

        model.GetReferencePose(positions2, rotations2);

        if (CalcPoseSingle(model, cycle, poseParameterValues, positions2, rotations2))
        {
            if ((Flags & Source1AnimationFlags.Local) != 0)
            {
                AddLayers(model, true, cycle, poseParameterValues, 1f, positions2, rotations2, depth);
            }

            SlerpBones(model, positions, rotations, positions2, rotations2, weight);
        }

        AddLayers(model, false, cycle, poseParameterValues, weight, positions, rotations, depth);
    }

    private void SlerpBones(Source1Model model, Span<Vector3> positions1, Span<Quaternion> rotations1, ReadOnlySpan<Vector3> positions2, ReadOnlySpan<Quaternion> rotations2, float s)
    {
        if (s <= 0f)
        {
            return;
        }

        var isDelta = IsDelta;
        var isPost = (Flags & Source1AnimationFlags.Post) != 0;

        for (var i = 0; i < model.Bones.Length; i++)
        {
            var s2 = s * BoneWeights[i];

            if (s2 <= 0f)
            {
                continue;
            }

            if (isDelta)
            {
                rotations1[i] = isPost
                    ? Source1PoseMath.MultiplyScale(rotations1[i], s2, rotations2[i])
                    : Source1PoseMath.ScaleMultiply(s2, rotations2[i], rotations1[i]);
                positions1[i] += positions2[i] * s2;
                continue;
            }

            var s1 = 1f - s2;

            if ((model.Bones[i].Flags & Source1PoseMath.FixedAlignmentBoneFlag) != 0)
            {
                rotations1[i] = Source1PoseMath.SlerpNoAlign(rotations2[i], rotations1[i], s1);
            }
            else
            {
                rotations1[i] = Source1PoseMath.Slerp(rotations2[i], rotations1[i], s1);
            }

            positions1[i] = (positions1[i] * s1) + (positions2[i] * s2);
        }
    }

    private void AddLayers(Source1Model model, bool local, float cycle, ReadOnlySpan<float> poseParameterValues, float weight, Span<Vector3> positions, Span<Quaternion> rotations, int depth)
    {
        foreach (var layer in AutoLayers)
        {
            var isLocal = (layer.Flags & Source1AutoLayerFlags.Local) != 0;

            if (isLocal != local)
            {
                continue;
            }

            var layerCycle = cycle;
            var layerWeight = weight;

            if (layer.Start != layer.End)
            {
                var usesPose = !local && (layer.Flags & Source1AutoLayerFlags.Pose) != 0;
                float index;

                if (!usesPose)
                {
                    index = cycle;
                }
                else if ((uint)layer.PoseParameterIndex < (uint)model.PoseParameters.Length)
                {
                    index = GetPoseValue(model, poseParameterValues, layer.PoseParameterIndex);
                }
                else
                {
                    index = 0f;
                }

                if (index < layer.Start || index >= layer.End)
                {
                    continue;
                }

                var s = 1f;

                if (index < layer.Peak && layer.Start != layer.Peak)
                {
                    s = (index - layer.Start) / (layer.Peak - layer.Start);
                }
                else if (index > layer.Tail && layer.End != layer.Tail)
                {
                    s = (layer.End - index) / (layer.End - layer.Tail);
                }

                if ((layer.Flags & Source1AutoLayerFlags.Spline) != 0)
                {
                    s = Source1PoseMath.SimpleSpline(s);
                }

                if ((layer.Flags & Source1AutoLayerFlags.XFade) != 0 && index > layer.Tail)
                {
                    layerWeight = s * weight / (1f - weight + (s * weight));
                }
                else if ((layer.Flags & Source1AutoLayerFlags.NoBlend) != 0)
                {
                    layerWeight = s;
                }
                else
                {
                    layerWeight = weight * s;
                }

                if (!usesPose)
                {
                    layerCycle = (cycle - layer.Start) / (layer.End - layer.Start);
                }
            }

            layer.Sequence?.AccumulateCore(model, layerCycle, poseParameterValues, layerWeight, positions, rotations, depth + 1);
        }
    }
}
