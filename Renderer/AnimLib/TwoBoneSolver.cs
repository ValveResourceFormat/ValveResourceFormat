using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib;

static class TwoBoneSolver
{
    /// <summary>
    /// Solves the two bone chain ending in the effector towards the target, in place on the pose.
    /// </summary>
    /// <param name="chainRotationWeight">How the effector rotation is solved: 0 fully rotates the effector, 1 rotates the chain.</param>
    public static void Solve(Pose pose, int effectorBoneIdx, Transform targetTransform, float chainRotationWeight = 0f, IKBlendMode blendMode = IKBlendMode.Effector, float blendWeight = 1f)
    {
        var skeleton = pose.Skeleton;

        // Set up bone indices
        Span<int> boneIndices = stackalloc int[3];
        boneIndices[2] = effectorBoneIdx;
        for (var i = 1; i >= 0; --i)
        {
            boneIndices[i] = skeleton.GetParentBoneIndex(boneIndices[i + 1]);
            Debug.Assert(boneIndices[i] != -1);
        }

        const int NumBonesInChain = 3;
        var startBoneIdx = boneIndices[0];

        // The parent of the start of the chain, needed to calculate the local transforms
        var baseParentIndex = skeleton.GetParentBoneIndex(startBoneIdx);
        var baseParentTransform = baseParentIndex != -1 ? pose.GetModelSpaceTransform(baseParentIndex) : Transform.Identity;

        Span<Transform> modelSpaceTransforms = stackalloc Transform[NumBonesInChain];
        var prevTransform = baseParentTransform;
        for (var i = 0; i < NumBonesInChain; i++)
        {
            modelSpaceTransforms[i] = pose.GetTransform(boneIndices[i]) * prevTransform;
            prevTransform = modelSpaceTransforms[i];
        }

        // Blend effector target
        var hasBlendWeight = blendWeight != 1f;
        if (hasBlendWeight && blendMode == IKBlendMode.Effector)
        {
            targetTransform = TransformMath.SLerp(modelSpaceTransforms[2], targetTransform, blendWeight);
        }

        Span<Transform> modelSpaceChainReferenceTransforms = stackalloc Transform[NumBonesInChain];
        for (var i = 0; i < NumBonesInChain; i++)
        {
            modelSpaceChainReferenceTransforms[i] = skeleton.ModelSpaceReferencePose[boneIndices[i]];
        }

        Solve(modelSpaceTransforms, modelSpaceChainReferenceTransforms, targetTransform, chainRotationWeight);

        // Blend results back into the pose, computing relative transforms in reverse order
        if (hasBlendWeight && blendMode == IKBlendMode.Pose)
        {
            for (var i = NumBonesInChain - 1; i > 0; i--)
            {
                var localTransformPostIK = TransformMath.Delta(modelSpaceTransforms[i - 1], modelSpaceTransforms[i]);
                pose.SetTransform(boneIndices[i], TransformMath.SLerp(pose.GetTransform(boneIndices[i]), localTransformPostIK, blendWeight));
            }

            var startLocalTransformPostIK = TransformMath.Delta(baseParentTransform, modelSpaceTransforms[0]);
            pose.SetTransform(startBoneIdx, TransformMath.SLerp(pose.GetTransform(boneIndices[0]), startLocalTransformPostIK, blendWeight));
        }
        else
        {
            for (var i = NumBonesInChain - 1; i > 0; i--)
            {
                pose.SetTransform(boneIndices[i], TransformMath.Delta(modelSpaceTransforms[i - 1], modelSpaceTransforms[i]));
            }

            pose.SetTransform(startBoneIdx, TransformMath.Delta(baseParentTransform, modelSpaceTransforms[0]));
        }
    }

    public static bool Solve(Span<Transform> modelSpaceBoneTransforms, ReadOnlySpan<Transform> modelSpaceReferenceTransforms, Transform targetTransform, float chainRotationWeight)
    {
        var v1 = modelSpaceBoneTransforms[1].Position - modelSpaceBoneTransforms[0].Position;
        var v2 = modelSpaceBoneTransforms[2].Position - modelSpaceBoneTransforms[1].Position;
        var vHDesired = targetTransform.Position - modelSpaceBoneTransforms[0].Position;

        var length1 = v1.Length();
        var length2 = v2.Length();
        var lenHDesired = vHDesired.Length();

        if (MathF.Abs(length1) <= TransformMath.Epsilon || length2 == 0f)
        {
            return false;
        }

        var v1Norm = v1 / length1;
        var v2Norm = v2 / length2;

        // The current bend in the mid joint
        var cosTheta = Math.Clamp(-Vector3.Dot(v1Norm, v2Norm), -1f, 1f);
        var theta = MathF.Acos(cosTheta);

        // The desired bend in the mid joint that puts the end effector at the required distance from the chain root
        var cosThetaDesired = Math.Clamp(((length1 * length1) + (length2 * length2) - (lenHDesired * lenHDesired)) / (2f * length1 * length2), -1f, 1f);
        var thetaDesired = MathF.Acos(cosThetaDesired);

        // How much we need to rotate in the mid joint
        var thetaDelta = thetaDesired - theta;

        Vector3 hingeAxisLS;
        Vector3 hingeAxisMS;
        if (MathF.Abs(cosTheta - (-1f)) <= 1e-5f)
        {
            // The chain is collinear, so the reference pose gives the hinge axis; this needs the
            // reference pose to have a slight bend
            var v1Ref = modelSpaceReferenceTransforms[1].Position - modelSpaceReferenceTransforms[0].Position;
            var v2Ref = modelSpaceReferenceTransforms[2].Position - modelSpaceReferenceTransforms[1].Position;
            var cross = Vector3.Cross(v2Ref, v1Ref);

            hingeAxisLS = cross.LengthSquared() > 1e-6f
                ? TransformMath.NormalizeOrZero(TransformMath.InverseRotateVector(modelSpaceReferenceTransforms[1].Angle, cross))
                : TransformMath.WorldLeft;

            hingeAxisMS = TransformMath.RotateVector(modelSpaceBoneTransforms[1].Angle, hingeAxisLS);
        }
        else
        {
            hingeAxisMS = TransformMath.NormalizeOrZero(Vector3.Cross(v2Norm, v1Norm));
            hingeAxisLS = TransformMath.InverseRotateVector(modelSpaceBoneTransforms[1].Angle, hingeAxisMS);
        }

        Span<Transform> parentSpaceBoneTransforms = stackalloc Transform[3];
        parentSpaceBoneTransforms[2] = modelSpaceBoneTransforms[2] * modelSpaceBoneTransforms[1].Inverse();

        // Rotate the mid joint so that the distance between the end effector and the root solves the chain
        modelSpaceBoneTransforms[1].Angle = Quaternion.CreateFromAxisAngle(hingeAxisMS, thetaDelta) * modelSpaceBoneTransforms[1].Angle;
        modelSpaceBoneTransforms[2] = parentSpaceBoneTransforms[2] * modelSpaceBoneTransforms[1];

        parentSpaceBoneTransforms[1] = modelSpaceBoneTransforms[1] * modelSpaceBoneTransforms[0].Inverse();

        // Rotate the root so that the end effector aligns with the target as much as possible
        var currentH = modelSpaceBoneTransforms[2].Position - modelSpaceBoneTransforms[0].Position;
        var rootRotation = TransformMath.FromRotationBetweenUnitVectors(TransformMath.NormalizeOrZero(currentH), TransformMath.NormalizeOrZero(vHDesired));
        modelSpaceBoneTransforms[0].Angle = rootRotation * modelSpaceBoneTransforms[0].Angle;
        modelSpaceBoneTransforms[1] = parentSpaceBoneTransforms[1] * modelSpaceBoneTransforms[0];
        modelSpaceBoneTransforms[2] = parentSpaceBoneTransforms[2] * modelSpaceBoneTransforms[1];
        modelSpaceBoneTransforms[2].Angle = targetTransform.Angle;

        // Without twist from the reference pose we are done
        if (chainRotationWeight <= 0f)
        {
            return true;
        }

        // Recalculate the model space hinge axis now that the chain is solved
        hingeAxisMS = TransformMath.RotateVector(modelSpaceBoneTransforms[1].Angle, hingeAxisLS);

        // The hinge axis (bend plane) that would match the reference pose as much as possible
        var referenceHingeAxisMS = TransformMath.NormalizeOrZero(TransformMath.RotateVector(
            modelSpaceBoneTransforms[2].Angle,
            TransformMath.InverseRotateVector(modelSpaceReferenceTransforms[2].Angle, TransformMath.RotateVector(modelSpaceReferenceTransforms[1].Angle, hingeAxisLS))));

        // Rotating the root joint around this axis leaves the end effector position unchanged
        var twistAxis = TransformMath.NormalizeOrZero(modelSpaceBoneTransforms[0].Position - modelSpaceBoneTransforms[2].Position);
        var twistDelta = TransformMath.CalculateAngleBetweenVectorsAroundAnAxis(referenceHingeAxisMS, hingeAxisMS, twistAxis);

        var twist = Quaternion.Slerp(Quaternion.Identity, Quaternion.CreateFromAxisAngle(twistAxis, -twistDelta), chainRotationWeight);
        modelSpaceBoneTransforms[0].Angle = twist * modelSpaceBoneTransforms[0].Angle;
        modelSpaceBoneTransforms[1] = parentSpaceBoneTransforms[1] * modelSpaceBoneTransforms[0];
        modelSpaceBoneTransforms[2] = parentSpaceBoneTransforms[2] * modelSpaceBoneTransforms[1];
        modelSpaceBoneTransforms[2].Angle = targetTransform.Angle;

        return true;
    }
}
