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
    /// Blends root motion transforms.
    /// </summary>
    public static Matrix4x4 BlendRootMotion(
        Matrix4x4 sourceRootMotion,
        Matrix4x4 targetRootMotion,
        float blendWeight,
        RootMotionBlendMode blendMode)
    {
        // Matches Esoterica's early-out ordering: a zero weight or IgnoreTarget yields the source
        if (blendWeight <= 0f || blendMode == RootMotionBlendMode.IgnoreTarget)
        {
            return sourceRootMotion;
        }

        if (blendWeight >= 1f || blendMode == RootMotionBlendMode.IgnoreSource)
        {
            return targetRootMotion;
        }

        if (blendMode == RootMotionBlendMode.Additive)
        {
            return LerpMatrix(sourceRootMotion, targetRootMotion * sourceRootMotion, blendWeight);
        }

        return LerpMatrix(sourceRootMotion, targetRootMotion, blendWeight);
    }

    static Matrix4x4 LerpMatrix(Matrix4x4 a, Matrix4x4 b, float t)
    {
        if (!Matrix4x4.Decompose(a, out var scaleA, out var rotA, out var transA))
        {
            return b;
        }

        if (!Matrix4x4.Decompose(b, out var scaleB, out var rotB, out var transB))
        {
            return a;
        }

        var lerpedTrans = Vector3.Lerp(transA, transB, t);
        var lerpedRot = Quaternion.Slerp(rotA, rotB, t);
        var lerpedScale = Vector3.Lerp(scaleA, scaleB, t);

        return Matrix4x4.CreateScale(lerpedScale)
            * Matrix4x4.CreateFromQuaternion(lerpedRot)
            * Matrix4x4.CreateTranslation(lerpedTrans);
    }
}
