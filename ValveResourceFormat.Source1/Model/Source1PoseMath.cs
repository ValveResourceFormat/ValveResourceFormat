namespace ValveResourceFormat.Source1.Model;

/// <summary>Quaternion operations with the semantics the Source engine blends bones with.</summary>
internal static class Source1PoseMath
{
    /// <summary>Bone flag: the bone cannot spin 360 degrees, so blends are not realigned to the shortest arc.</summary>
    public const int FixedAlignmentBoneFlag = 0x00100000;

    /// <summary>Hamilton product, which applies <paramref name="q"/> first and then <paramref name="p"/>. <paramref name="q"/> is aligned to <paramref name="p"/> first.</summary>
    public static Quaternion Multiply(Quaternion p, Quaternion q)
    {
        q = Align(p, q);

        return new Quaternion(
            (p.X * q.W) + (p.Y * q.Z) - (p.Z * q.Y) + (p.W * q.X),
            (-p.X * q.Z) + (p.Y * q.W) + (p.Z * q.X) + (p.W * q.Y),
            (p.X * q.Y) - (p.Y * q.X) + (p.Z * q.W) + (p.W * q.Z),
            (-p.X * q.X) - (p.Y * q.Y) - (p.Z * q.Z) + (p.W * q.W));
    }

    /// <summary>Negates <paramref name="q"/> when it lies more than 180 degrees from <paramref name="p"/>.</summary>
    public static Quaternion Align(Quaternion p, Quaternion q)
    {
        return Quaternion.Dot(p, q) < 0f ? Quaternion.Negate(q) : q;
    }

    /// <summary>Spherical interpolation along the shortest arc.</summary>
    public static Quaternion Slerp(Quaternion p, Quaternion q, float t)
    {
        return SlerpNoAlign(p, Align(p, q), t);
    }

    /// <summary>Spherical interpolation that keeps the orientation of <paramref name="q"/>.</summary>
    public static Quaternion SlerpNoAlign(Quaternion p, Quaternion q, float t)
    {
        var cosom = Quaternion.Dot(p, q);

        if (1f + cosom > 0.000001f)
        {
            float scaleP;
            float scaleQ;

            if (1f - cosom > 0.000001f)
            {
                var omega = MathF.Acos(Math.Clamp(cosom, -1f, 1f));
                var sinom = MathF.Sin(omega);
                scaleP = MathF.Sin((1f - t) * omega) / sinom;
                scaleQ = MathF.Sin(t * omega) / sinom;
            }
            else
            {
                scaleP = 1f - t;
                scaleQ = t;
            }

            return new Quaternion(
                (scaleP * p.X) + (scaleQ * q.X),
                (scaleP * p.Y) + (scaleQ * q.Y),
                (scaleP * p.Z) + (scaleQ * q.Z),
                (scaleP * p.W) + (scaleQ * q.W));
        }

        var perpendicular = new Quaternion(-q.Y, q.X, -q.W, q.Z);
        var halfPi = 0.5f * MathF.PI;
        var scaleFrom = MathF.Sin((1f - t) * halfPi);
        var scaleTo = MathF.Sin(t * halfPi);

        return new Quaternion(
            (scaleFrom * p.X) + (scaleTo * perpendicular.X),
            (scaleFrom * p.Y) + (scaleTo * perpendicular.Y),
            (scaleFrom * p.Z) + (scaleTo * perpendicular.Z),
            perpendicular.W);
    }

    /// <summary>Normalized linear interpolation along the shortest arc.</summary>
    public static Quaternion Blend(Quaternion p, Quaternion q, float t)
    {
        return BlendNoAlign(p, Align(p, q), t);
    }

    /// <summary>Normalized linear interpolation that keeps the orientation of <paramref name="q"/>.</summary>
    public static Quaternion BlendNoAlign(Quaternion p, Quaternion q, float t)
    {
        var scaleP = 1f - t;
        var blended = new Quaternion(
            (scaleP * p.X) + (t * q.X),
            (scaleP * p.Y) + (t * q.Y),
            (scaleP * p.Z) + (t * q.Z),
            (scaleP * p.W) + (t * q.W));

        return Normalize(blended);
    }

    /// <summary>Normalizes a quaternion, leaving a zero quaternion as identity.</summary>
    public static Quaternion Normalize(Quaternion q)
    {
        var length = MathF.Sqrt(Quaternion.Dot(q, q));

        if (length <= 0f)
        {
            return Quaternion.Identity;
        }

        var inverse = 1f / length;
        return new Quaternion(q.X * inverse, q.Y * inverse, q.Z * inverse, q.W * inverse);
    }

    /// <summary>Smooth step from 0 to 1.</summary>
    public static float SimpleSpline(float s)
    {
        return (3f * s * s) - (2f * s * s * s);
    }
}
