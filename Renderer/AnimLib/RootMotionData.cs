using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class RootMotionData
{
    public Transform[] Transforms { get; } = [];
    public int NumFrames { get; private set; }
    public float AverageLinearVelocity { get; private set; }
    public float AverageAngularVelocityRadians { get; private set; }
    public Transform TotalDelta { get; private set; }

    public RootMotionData(Transform[] transforms, int numFrames, RootMotionData? source = null)
    {
        Transforms = transforms;
        NumFrames = numFrames;
        AverageLinearVelocity = source?.AverageLinearVelocity ?? 0f;
        AverageAngularVelocityRadians = source?.AverageAngularVelocityRadians ?? 0f;
        TotalDelta = source?.TotalDelta ?? Transform.Identity;
    }

    public static RootMotionData Empty { get; } = new([], 0);

    /// <summary>Reuses this buffer for root motion written into <see cref="Transforms"/> by the caller.</summary>
    public void SetFrom(int numFrames, RootMotionData? source)
    {
        NumFrames = numFrames;
        AverageLinearVelocity = source?.AverageLinearVelocity ?? 0f;
        AverageAngularVelocityRadians = source?.AverageAngularVelocityRadians ?? 0f;
        TotalDelta = source?.TotalDelta ?? Transform.Identity;
    }

    /// <summary>Valid data, which is distinct from having any actual motion.</summary>
    public bool IsValid => NumFrames > 0;

    public bool IsStationary => Transforms.Length <= 1;

    public Transform GetTransform(FrameTime frameTime)
    {
        if (Transforms.Length == 1)
        {
            return Transforms[0];
        }

        if (frameTime.IsExactlyAtKeyFrame)
        {
            return Transforms[frameTime.FrameIndex];
        }

        var frameStartTransform = Transforms[frameTime.FrameIndex];
        var frameEndTransform = Transforms[frameTime.FrameIndex + 1];
        return TransformMath.SLerp(frameStartTransform, frameEndTransform, frameTime.PercentageThrough);
    }

    public Transform GetTransform(float percentageThrough) => GetTransform(new FrameTime(percentageThrough, NumFrames));

    /// <summary>The root motion delta for a time range; handles a single loop.</summary>
    public Transform GetDelta(float fromTime, float toTime)
    {
        if (Transforms.Length <= 1)
        {
            return Transform.Identity;
        }

        if (fromTime <= toTime)
        {
            return GetDeltaNoLooping(fromTime, toTime);
        }

        var preLoopDelta = GetDeltaNoLooping(fromTime, 1f);
        var postLoopDelta = GetDeltaNoLooping(0f, toTime);
        return postLoopDelta * preLoopDelta;
    }

    public Transform GetDeltaNoLooping(float fromTime, float toTime)
    {
        if (Transforms.Length <= 1)
        {
            return Transform.Identity;
        }

        var startTransform = GetTransform(fromTime);
        var endTransform = GetTransform(toTime);
        return TransformMath.DeltaNoScale(startTransform, endTransform);
    }

    /// <summary>Samples either the plain delta, or the delta that moves the character to the expected world space root position.</summary>
    public Transform SampleRootMotion(RootMotionData__SamplingMode mode, Transform currentWorldTransform, float startTime, float endTime)
    {
        if (Transforms.Length <= 1)
        {
            return Transform.Identity;
        }

        if (mode == RootMotionData__SamplingMode.WorldSpace)
        {
            var desiredFinalTransform = GetTransform(endTime);
            return TransformMath.DeltaNoScale(currentWorldTransform, desiredFinalTransform);
        }

        return GetDelta(startTime, endTime);
    }

    /// <summary>
    /// The direction of movement into a frame from the previous one, or the facing when there is no movement.
    /// </summary>
    public Quaternion GetIncomingMovementOrientation2DAtFrame(int frameIdx)
    {
        if (Transforms.Length == 0)
        {
            return Quaternion.Identity;
        }

        if (Transforms.Length == 1)
        {
            return Transforms[0].Angle;
        }

        if (frameIdx == 0)
        {
            return Transforms[frameIdx].Angle;
        }

        var movementDelta = Transforms[frameIdx].Position - Transforms[frameIdx - 1].Position;
        if (MathF.Abs(movementDelta.X) <= TransformMath.Epsilon && MathF.Abs(movementDelta.Y) <= TransformMath.Epsilon)
        {
            return Transforms[frameIdx].Angle;
        }

        return TransformMath.FromRotationBetweenUnitVectors(TransformMath.WorldForward, TransformMath.Normalize2(movementDelta));
    }
}
