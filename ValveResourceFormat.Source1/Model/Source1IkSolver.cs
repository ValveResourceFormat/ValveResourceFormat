namespace ValveResourceFormat.Source1.Model;

/// <summary>Solves two-bone chains such as an arm or a leg so that the last bone reaches a target.</summary>
internal static class Source1IkSolver
{
    // A chain this close to straight is too stretched to find which way it bends (about one degree)
    private const float MaxReach = 0.9998f;

    /// <summary>
    /// Moves the knee and foot of a chain to reach a target position and aims the hip and knee bones at the next joint.
    /// The bend direction is the one the chain has in the pose it is given.
    /// </summary>
    /// <param name="world">Model-space transforms of the bones. The three chain bones are updated when the solve succeeds.</param>
    /// <param name="hip">First bone of the chain.</param>
    /// <param name="knee">Second bone of the chain.</param>
    /// <param name="foot">Last bone of the chain.</param>
    /// <param name="target">Position the foot should reach.</param>
    /// <returns>False when the chain cannot be solved, in which case nothing is changed.</returns>
    public static bool Solve(Span<Matrix4x4> world, int hip, int knee, int foot, Vector3 target)
    {
        var hipPosition = world[hip].Translation;
        var kneePosition = world[knee].Translation;
        var footPosition = world[foot].Translation;

        var hipToKnee = kneePosition - hipPosition;
        var hipToFoot = footPosition - hipPosition;
        var upperLength = hipToKnee.Length();
        var lowerLength = (footPosition - kneePosition).Length();
        var straightLength = hipToFoot.Length();

        if (upperLength <= 0f || lowerLength <= 0f || straightLength <= 0f)
        {
            return false;
        }

        if (straightLength > (upperLength + lowerLength) * MaxReach)
        {
            return false;
        }

        var kneeDirection = hipToKnee - (hipToFoot * (upperLength / straightLength));
        var kneeDirectionLength = kneeDirection.Length();

        if (kneeDirectionLength <= 0f)
        {
            return false;
        }

        kneeDirection /= kneeDirectionLength;

        // Nearly straight limbs get a far away bend target so the knee keeps its side
        var reach = (target - hipPosition).Length() - MathF.Min(upperLength, lowerLength);
        reach = MathF.Max(upperLength + lowerLength, reach) * 100f;

        var bendTarget = hipToKnee + (kneeDirection * reach);
        var hipToTarget = target - hipPosition;
        var maxLength = (upperLength + lowerLength) * MaxReach;

        if (hipToTarget.Length() > maxLength)
        {
            hipToTarget = Vector3.Normalize(hipToTarget) * maxLength;
        }

        // About an 80 degree knee bend at most
        var minLength = MathF.Max(MathF.Abs(upperLength - lowerLength) * 1.15f, MathF.Min(upperLength, lowerLength) * 0.15f);

        if (hipToTarget.Length() < minLength)
        {
            hipToTarget = hipToFoot / straightLength * minLength;
        }

        if (!TrySolveKnee(upperLength, lowerLength, hipToTarget, bendTarget, out var newHipToKnee))
        {
            return false;
        }

        if (!TryAlign(world[hip], newHipToKnee, out var alignedHip) || !TryAlign(world[knee], hipToTarget - newHipToKnee, out var alignedKnee))
        {
            return false;
        }

        alignedKnee.Translation = hipPosition + newHipToKnee;

        var alignedFoot = world[foot];
        alignedFoot.Translation = hipPosition + hipToTarget;

        world[hip] = alignedHip;
        world[knee] = alignedKnee;
        world[foot] = alignedFoot;

        return true;
    }

    // Finds the knee at the given distances from the hip and the foot, as close as possible to the bend target
    private static bool TrySolveKnee(float upperLength, float lowerLength, Vector3 footPosition, Vector3 bendTarget, out Vector3 knee)
    {
        knee = default;

        var r = footPosition.Length();
        var x = footPosition / r;
        var y = bendTarget - (x * Vector3.Dot(bendTarget, x));
        var yLength = y.Length();

        if (yLength <= 0f)
        {
            return false;
        }

        y /= yLength;

        var along = (r + (((upperLength * upperLength) - (lowerLength * lowerLength)) / r)) / 2f;
        var across = MathF.Sqrt((upperLength * upperLength) - (along * along));

        knee = (x * along) + (y * across);

        return along > r - lowerLength && along < upperLength;
    }

    // Points the x axis of a transform along a direction and keeps its z axis as close as it can
    private static bool TryAlign(Matrix4x4 transform, Vector3 direction, out Matrix4x4 aligned)
    {
        aligned = transform;

        var x = Vector3.Normalize(direction);
        var z = new Vector3(transform.M31, transform.M32, transform.M33);
        var y = Vector3.Cross(z, x);
        var yLength = y.Length();

        if (float.IsNaN(x.X) || yLength <= 0.0001f)
        {
            return false;
        }

        y /= yLength;
        z = Vector3.Cross(x, y);

        aligned.M11 = x.X;
        aligned.M12 = x.Y;
        aligned.M13 = x.Z;
        aligned.M21 = y.X;
        aligned.M22 = y.Y;
        aligned.M23 = y.Z;
        aligned.M31 = z.X;
        aligned.M32 = z.Y;
        aligned.M33 = z.Z;

        return true;
    }
}
