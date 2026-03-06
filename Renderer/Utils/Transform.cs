using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Alias for the ValveResourceFormat FrameBone
/// </summary>
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

    public static implicit operator FrameBone(Transform t) => new(t.Position, t.Scale, t.Rotation);
    public static implicit operator Transform(FrameBone fb) => new(fb.Position, fb.Scale, fb.Angle);
    public static implicit operator Matrix4x4(Transform t) => t.ToMatrix();

    public static Transform operator *(Transform lhs, Transform rhs) => (FrameBone)lhs * (FrameBone)rhs;

    public readonly Matrix4x4 ToMatrix() => ((FrameBone)this).ToMatrix();

    public readonly Transform Inverse()
    {
        var invScale = 1.0f / Scale;
        var invRotation = Quaternion.Inverse(Rotation);
        var invPosition = Vector3.Transform(-Position * invScale, invRotation);

        return new Transform(invPosition, invScale, invRotation);
    }
}
