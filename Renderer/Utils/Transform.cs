using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer;

record struct Transform(Vector3 Position, float Scale, Quaternion Rotation)
{
    public static Transform Identity => new(Vector3.Zero, 1.0f, Quaternion.Identity);

    public readonly Vector4 PositionScale => new(Position, Scale);
    public readonly Vector3 ScaleVector => new(Scale);

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

    public static Transform operator *(Transform lhs, Transform rhs)
    {
        var finalScale = lhs.Scale * rhs.Scale;

        if (lhs.Scale < 0 || rhs.Scale < 0)
        {
            // Multiply the transforms using matrices to get the correct rotation
            var lhsMtx = lhs.ToMatrix();
            var rhsMtx = rhs.ToMatrix();
            var resultMtx = lhsMtx * rhsMtx;

            // Decompose to extract components
            Matrix4x4.Decompose(resultMtx, out _, out var rotation, out var _translation);

            // Apply back the sign from the final scale
            var sign = MathF.Sign(finalScale);
            if (sign < 0)
            {
                // Flip the rotation by negating XYZ components
                rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, rotation.W);
            }

            return new Transform(_translation, MathF.Abs(finalScale), Quaternion.Normalize(rotation));
        }

        // Normal case
        var combinedRotation = Quaternion.Normalize(lhs.Rotation * rhs.Rotation);
        var translation = Vector3.Transform(lhs.Position * rhs.Scale, rhs.Rotation) + rhs.Position;

        return new Transform(translation, finalScale, combinedRotation);
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
