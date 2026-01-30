using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer;

record struct Transform(Vector3 Position, float Scale, Quaternion Rotation)
{
    public static Transform Identity => new(Vector3.Zero, 1.0f, Quaternion.Identity);

    public Transform(KVObject data)
        : this(Vector3.Zero, 1.0f, Quaternion.Identity)
    {
        Position = new Vector3(
            data.GetProperty<float>("0"),
            data.GetProperty<float>("1"),
            data.GetProperty<float>("2")
        );

        Scale = data.GetProperty<float>("3");

        Rotation = new Quaternion(
            data.GetProperty<float>("4"),
            data.GetProperty<float>("5"),
            data.GetProperty<float>("6"),
            data.GetProperty<float>("7")
        );
    }

    public static Transform operator *(Transform t, Matrix4x4 m)
    {
        // Apply the matrix to the transform by multiplying matrices then decomposing back.
        var combined = t.ToMatrix() * m;
        if (Matrix4x4.Decompose(combined, out var scaleVec, out var rot, out var trans))
        {
            // Transform stores a uniform scale; take X (decompose returns per-axis scale).
            var scale = scaleVec.X;
            return new Transform(trans, scale, Quaternion.Normalize(rot));
        }

        // Decompose failed — return original transform unchanged (safe fallback).
        return t;
    }

    public static Transform operator *(Matrix4x4 m, Transform t)
    {
        var combined = m * t.ToMatrix();
        if (Matrix4x4.Decompose(combined, out var scaleVec, out var rot, out var trans))
        {
            var scale = scaleVec.X;
            return new Transform(trans, scale, Quaternion.Normalize(rot));
        }

        return t;
    }

    public readonly Matrix4x4 ToMatrix()
    {
        var scaleMatrix = Matrix4x4.CreateScale(Scale);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
        var translationMatrix = Matrix4x4.CreateTranslation(Position);

        return scaleMatrix * rotationMatrix * translationMatrix;
    }

    public readonly Transform Inverse()
    {
        var invScale = 1.0f / Scale;
        var invRotation = Quaternion.Inverse(Rotation);
        var invPosition = Vector3.Transform(-Position * invScale, invRotation);

        return new Transform(invPosition, invScale, invRotation);
    }
}
