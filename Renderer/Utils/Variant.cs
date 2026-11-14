using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Utils;

/// <summary>An integer, float or vector value.</summary>
[StructLayout(LayoutKind.Explicit)]
public readonly record struct Variant
{
    internal enum VariantType : byte
    {
        Int,
        Float,
        Vector,
    }

    // The integer shares the first 8 bytes of the vector
    [FieldOffset(0)]
    private readonly Vector4 vector;

    [FieldOffset(0)]
    private readonly long intValue;

    [FieldOffset(16)]
    private readonly VariantType type;

    internal VariantType Type => type;

    internal long Int => intValue;

    /// <summary>Gets the vector, or the float in X.</summary>
    internal Vector4 Vector => vector;

    /// <summary>Gets the value as a vector, with scalars in every component.</summary>
    internal Vector4 ToVector4() => type switch
    {
        VariantType.Int => new Vector4(intValue),
        VariantType.Float => new Vector4(vector.X),
        _ => vector,
    };

    private Variant(long value)
    {
        this = default;
        intValue = value;
        type = VariantType.Int;
    }

    private Variant(VariantType type, Vector4 value)
    {
        this = default;
        vector = value;
        this.type = type;
    }

    /// <summary>Creates an integer value.</summary>
    public static Variant FromInt64(long value) => new(value);

    /// <summary>Creates an integer value of 1 or 0.</summary>
    public static Variant FromBoolean(bool value) => new(value ? 1 : 0);

    /// <summary>Creates a float value.</summary>
    public static Variant FromSingle(float value) => new(VariantType.Float, new Vector4(value, 0, 0, 0));

    /// <summary>Creates a vector value.</summary>
    public static Variant FromVector4(Vector4 value) => new(VariantType.Vector, value);

    /// <summary>Converts an integer.</summary>
    public static implicit operator Variant(long value) => FromInt64(value);

    /// <summary>Converts a boolean to 1 or 0.</summary>
    public static implicit operator Variant(bool value) => FromBoolean(value);

    /// <summary>Converts a float.</summary>
    public static implicit operator Variant(float value) => FromSingle(value);

    /// <summary>Converts a vector.</summary>
    public static implicit operator Variant(Vector4 value) => FromVector4(value);
}
