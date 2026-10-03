using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer.AnimLib;

static class Blender
{
    /// <summary>
    /// Blends two poses using standard linear interpolation.
    /// </summary>
    public static void Blend(
        ReadOnlySpan<FrameBone> sourcePose,
        ReadOnlySpan<FrameBone> targetPose,
        float blendWeight,
        Span<FrameBone> resultPose)
    {
        for (var i = 0; i < resultPose.Length; i++)
        {
            resultPose[i] = sourcePose[i].Blend(targetPose[i], blendWeight);
        }
    }

    /// <summary>
    /// Blends two poses with per-bone weights scaled by a bone mask.
    /// </summary>
    public static void BlendMasked(
        ReadOnlySpan<FrameBone> sourcePose,
        ReadOnlySpan<FrameBone> targetPose,
        float blendWeight,
        ReadOnlySpan<float> boneMaskWeights,
        Span<FrameBone> resultPose)
    {
        // A full weight takes the target as is, ignoring the mask
        if (blendWeight == 0f || boneMaskWeights.IndexOfAnyExcept(0f) == -1)
        {
            sourcePose[..resultPose.Length].CopyTo(resultPose);
            return;
        }

        if (blendWeight == 1f)
        {
            targetPose[..resultPose.Length].CopyTo(resultPose);
            return;
        }

        for (var i = 0; i < resultPose.Length; i++)
        {
            var boneBlendWeight = blendWeight * boneMaskWeights[i];
            resultPose[i] = boneBlendWeight == 0f ? sourcePose[i] : sourcePose[i].Blend(targetPose[i], boneBlendWeight);
        }
    }

    /// <summary>
    /// Blends two poses using additive blending.
    /// </summary>
    public static void AdditiveBlend(
        ReadOnlySpan<FrameBone> sourcePose,
        ReadOnlySpan<FrameBone> targetPose,
        float blendWeight,
        Span<FrameBone> resultPose)
    {
        for (var i = 0; i < resultPose.Length; i++)
        {
            resultPose[i] = sourcePose[i].BlendAdd(targetPose[i], blendWeight);
        }
    }

    /// <summary>
    /// Blends two root motion deltas.
    /// </summary>
    public static Transform BlendRootMotion(
        Transform source,
        Transform target,
        float blendWeight,
        RootMotionBlendMode blendMode = RootMotionBlendMode.Blend)
    {
        if (blendWeight <= 0f || blendMode == RootMotionBlendMode.IgnoreTarget)
        {
            return source;
        }

        if (blendWeight >= 1f || blendMode == RootMotionBlendMode.IgnoreSource)
        {
            return target;
        }

        // Root motion deltas carry no scale
        if (blendMode == RootMotionBlendMode.Additive)
        {
            var additiveTarget = source.Angle * target.Angle;
            return new Transform(
                Vector3.FusedMultiplyAdd(target.Position, new Vector3(blendWeight), source.Position),
                1f,
                Quaternion.Slerp(source.Angle, additiveTarget, blendWeight));
        }

        return new Transform(
            Vector3.Lerp(source.Position, target.Position, blendWeight),
            1f,
            TransformMath.FastSLerp(source.Angle, target.Angle, blendWeight));
    }
}
