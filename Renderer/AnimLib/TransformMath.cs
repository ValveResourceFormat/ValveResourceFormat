namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>Transform helpers that have no equivalent on <see cref="Transform"/> itself.</summary>
static class TransformMath
{
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
}
