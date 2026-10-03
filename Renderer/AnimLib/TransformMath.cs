namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>Transform helpers that have no equivalent on <see cref="Transform"/> itself.</summary>
static class TransformMath
{
    // Source axes, which the compiled graph and clip data use
    public static readonly Vector3 WorldForward = Vector3.UnitX;
    public static readonly Vector3 WorldRight = -Vector3.UnitY;
    public static readonly Vector3 WorldLeft = Vector3.UnitY;
    public static readonly Vector3 WorldUp = Vector3.UnitZ;

    public const float Epsilon = 1.0e-06f;

    public static bool IsNearZero(Vector3 v, float epsilon = Epsilon)
        => MathF.Abs(v.X) <= epsilon && MathF.Abs(v.Y) <= epsilon && MathF.Abs(v.Z) <= epsilon;

    /// <summary>The delta that concatenated onto <paramref name="from"/> gives <paramref name="to"/>, ignoring scale.</summary>
    public static Transform DeltaNoScale(Transform from, Transform to)
    {
        var inverseFromRotation = Quaternion.Inverse(from.Angle);
        var deltaTranslation = to.Position - from.Position;

        return new Transform(
            Vector3.Transform(deltaTranslation, inverseFromRotation),
            1f,
            inverseFromRotation * to.Angle);
    }

    /// <summary>Interpolates translation and scale linearly and rotation spherically.</summary>
    public static Transform SLerp(Transform from, Transform to, float t) => from.Blend(to, t);

    /// <summary>An approximated spherical interpolation, renormalized.</summary>
    public static Quaternion FastSLerp(Quaternion q0, Quaternion q1, float t)
    {
        var dot = Quaternion.Dot(q0, q1);

        var d = MathF.Abs(dot);
        var a = 1.0904f + d * (-3.2452f + d * (3.55645f - d * 1.43519f));
        var b = 0.848013f + d * (-1.06021f + d * 0.215638f);
        var k = a * (t - 0.5f) * (t - 0.5f) + b;
        var ot = t + t * (t - 0.5f) * (t - 1f) * k;

        var qt0 = 1f - ot;
        var qt1 = dot > 0f ? ot : -ot;

        return Quaternion.Normalize(new Quaternion(
            q0.X * qt0 + q1.X * qt1,
            q0.Y * qt0 + q1.Y * qt1,
            q0.Z * qt0 + q1.Z * qt1,
            q0.W * qt0 + q1.W * qt1));
    }

    /// <summary>The shortest rotation aligning one unit vector onto another.</summary>
    public static Quaternion FromRotationBetweenUnitVectors(Vector3 from, Vector3 to)
    {
        var dot = Vector3.Dot(from, to);

        // Parallel vectors
        if (dot >= 1f - Epsilon)
        {
            return Quaternion.Identity;
        }

        // Opposite vectors, 180 degrees around any orthogonal axis
        if (dot <= Epsilon - 1f)
        {
            return Quaternion.Normalize(new Quaternion(-from.Z, from.Y, from.X, 0f));
        }

        var cross = Vector3.Cross(from, to);
        var q = new Vector4(cross, dot);
        q.W += q.Length();
        return Quaternion.Normalize(new Quaternion(q.X, q.Y, q.Z, q.W));
    }

    /// <summary>The rotation aligning one unit vector onto another, around a fallback axis for opposite vectors.</summary>
    public static Quaternion FromRotationBetweenUnitVectors(Vector3 from, Vector3 to, Vector3 fallbackRotationAxis)
    {
        var rotationAxis = Vector3.Cross(from, to);
        var axisLengthSquared = rotationAxis.LengthSquared();
        rotationAxis = axisLengthSquared > 0f ? rotationAxis / MathF.Sqrt(axisLengthSquared) : fallbackRotationAxis;

        var dot = Vector3.Dot(from, to);
        if (dot >= 1f - Epsilon)
        {
            return Quaternion.Identity;
        }

        return Quaternion.CreateFromAxisAngle(rotationAxis, MathF.Acos(dot));
    }

    /// <summary>The rotation angle of a quaternion in radians, in [0, 2pi].</summary>
    public static float GetAngle(Quaternion q) => 2f * MathF.Acos(q.W);

    /// <summary>The delta that concatenated onto <paramref name="from"/> gives <paramref name="to"/>.</summary>
    public static Transform Delta(Transform from, Transform to)
    {
        var inverseScale = 1f / from.Scale;
        var fromInverseRotation = Quaternion.Inverse(from.Angle);

        return new Transform(
            Vector3.Transform(to.Position - from.Position, fromInverseRotation) * inverseScale,
            to.Scale * inverseScale,
            fromInverseRotation * to.Angle);
    }

    public static Vector3 RotateVector(Quaternion q, Vector3 v) => Vector3.Transform(v, q);

    public static Vector3 InverseRotateVector(Quaternion q, Vector3 v) => Vector3.Transform(v, Quaternion.Conjugate(q));

    public static Vector3 NormalizeOrZero(Vector3 v)
    {
        var lengthSquared = v.LengthSquared();
        return lengthSquared > 0f ? v / MathF.Sqrt(lengthSquared) : Vector3.Zero;
    }

    /// <summary>The signed angle between two vectors around an axis.</summary>
    public static float CalculateAngleBetweenVectorsAroundAnAxis(Vector3 sourceVector, Vector3 targetVector, Vector3 rotationAxis)
    {
        var altSrc = NormalizeOrZero(Vector3.Cross(sourceVector, rotationAxis));
        var altDst = NormalizeOrZero(Vector3.Cross(targetVector, rotationAxis));

        var dp = Vector3.Dot(altSrc, altDst);
        var angle = MathF.Acos(Math.Clamp(dp, -1f, 1f));

        // If the cross product between the vectors is facing away from the rotation axis then the angle is negative
        var cp = Vector3.Cross(altSrc, altDst);
        if (Vector3.Dot(cp, rotationAxis) < 0f)
        {
            angle *= -1f;
        }

        return angle;
    }
}
