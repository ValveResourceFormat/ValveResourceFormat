using System.Globalization;
using System.IO;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Typed Source material values with case-insensitive keys.
/// </summary>
internal readonly struct VmtParameters(IReadOnlyDictionary<string, string> values, string path)
{
    public string? String(string key, string? fallback = null) => values.TryGetValue(key, out var value) ? value : fallback;

    public bool Contains(string key) => values.ContainsKey(key);

    public float Float(string key, float fallback = 0)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }

        var text = value.AsSpan().Trim();

        if (text.StartsWith("[", StringComparison.Ordinal) || text.StartsWith("{", StringComparison.Ordinal))
        {
            Span<float> components = stackalloc float[4];
            var count = ReadComponents(text, key, components, out var byteColor);

            if (count is < 1 or > 4)
            {
                return fallback;
            }

            return byteColor ? components[0] / 255 : components[0];
        }

        for (var length = text.Length; length > 0; length--)
        {
            if (!float.TryParse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                continue;
            }

            if (!float.IsFinite(parsed))
            {
                throw Invalid(key, $"invalid finite numeric value '{text}'");
            }

            return parsed;
        }

        return 0;
    }

    public int Integer(string key, int fallback = 0)
    {
        var value = Float(key, fallback);

        if (value < int.MinValue || value >= 2147483648.0f)
        {
            throw Invalid(key, "expected an integer");
        }

        return (int)value;
    }

    public bool Boolean(string key, bool fallback = false)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }

        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        // Material flags are integers, so a fraction below one is off
        return MathF.Truncate(Float(key)) != 0;
    }

    /// <summary>
    /// Reads a color or vector. The <c>{r g b}</c> form is in 0-255 and is returned normalized to 0-1.
    /// </summary>
    public Vector3 Vector(string key, Vector3 fallback)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Vector3.Zero;
        }

        Span<float> components = stackalloc float[3];
        components.Clear();
        var count = ReadComponents(text, key, components, out var byteColor);

        if (count is < 1 or > 4)
        {
            return fallback;
        }

        var scalar = count == 1 && !text.AsSpan().TrimStart().StartsWith("[", StringComparison.Ordinal) && !byteColor;
        var value = scalar ? new Vector3(components[0]) : new Vector3(components[0], components[1], components[2]);

        return byteColor ? value / 255.0f : value;
    }

    /// <summary>
    /// Reads a color with an alpha. The <c>{r g b a}</c> form is in 0-255 and is returned normalized to 0-1.
    /// </summary>
    public Vector4 Color4(string key, Vector4 fallback)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        Span<float> components = stackalloc float[4];
        components.Clear();
        var count = ReadComponents(text, key, components, out var byteColor);

        if (count != 4)
        {
            return fallback;
        }

        var value = new Vector4(components[0], components[1], components[2], components[3]);

        return byteColor ? value / 255.0f : value;
    }

    public Vector2 Pair(string key, Vector2 fallback)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return fallback;
        }

        Span<float> components = stackalloc float[4];
        var count = ReadComponents(text, key, components, out _);

        if (count is < 2 or > 4)
        {
            return fallback;
        }

        return new Vector2(components[0], components[1]);
    }

    /// <summary>
    /// Reads a texture transform, either as a 4x2 matrix or as center/scale/rotate/translate operations. Rotation is in degrees.
    /// </summary>
    public Matrix4x4 Transform(string key)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return Matrix4x4.Identity;
        }

        if (text.AsSpan().Trim().StartsWith("[", StringComparison.Ordinal))
        {
            Span<float> components = stackalloc float[16];

            if (ReadComponents(text, key, components, out _) != 16)
            {
                throw Invalid(key, "expected sixteen texture matrix components");
            }

            return new Matrix4x4(
                components[0], components[4], 0, 0,
                components[1], components[5], 0, 0,
                0, 0, 1, 0,
                components[3], components[7], 0, 1);
        }

        // Only the two fixed operation orders are transforms, any other text leaves the texture untransformed
        Span<float> numbers = stackalloc float[7];
        var remaining = text.AsSpan();
        Matrix4x4 matrix;

        if (TryReadCenteredTransform(remaining, numbers))
        {
            var center = new Vector3(numbers[0], numbers[1], 0);
            var translation = new Vector3(numbers[5], numbers[6], 0);

            matrix = Matrix4x4.CreateTranslation(-center)
                * Matrix4x4.CreateScale(numbers[2], numbers[3], 1)
                * Matrix4x4.CreateRotationZ(numbers[4] * (MathF.PI / 180f))
                * Matrix4x4.CreateTranslation(center + translation);
        }
        else if (TryReadUncenteredTransform(remaining, numbers))
        {
            // The center is implied: the texture is moved by the translation relative to its middle, scaled and rotated, and
            // the offset that puts the middle back is rotated the opposite way
            var scaleX = numbers[0];
            var scaleY = numbers[1];
            var radians = numbers[4] * (MathF.PI / 180f);
            var offset = new Vector2(0.5f / (scaleX != 0 ? scaleX : 1f), 0.5f / (scaleY != 0 ? scaleY : 1f));
            offset = Vector2.Transform(offset, Matrix3x2.CreateRotation(-radians));

            matrix = Matrix4x4.CreateTranslation(offset.X, offset.Y, 0)
                * Matrix4x4.CreateRotationZ(radians)
                * Matrix4x4.CreateScale(scaleX, scaleY, 1)
                * Matrix4x4.CreateTranslation(numbers[2] - 0.5f, numbers[3] - 0.5f, 0);
        }
        else
        {
            return Matrix4x4.Identity;
        }

        if (!float.IsFinite(matrix.M11) || !float.IsFinite(matrix.M12)
            || !float.IsFinite(matrix.M21) || !float.IsFinite(matrix.M22)
            || !float.IsFinite(matrix.M41) || !float.IsFinite(matrix.M42))
        {
            throw Invalid(key, "texture transform overflows");
        }

        return matrix;
    }

    // center x y, scale x y, rotate angle, translate x y
    private static bool TryReadCenteredTransform(ReadOnlySpan<char> text, Span<float> numbers)
    {
        return TryReadOperation(ref text, "center", numbers[..2])
            && TryReadOperation(ref text, "scale", numbers[2..4])
            && TryReadOperation(ref text, "rotate", numbers[4..5])
            && TryReadOperation(ref text, "translate", numbers[5..7]);
    }

    // scale x y, translate x y, rotate angle
    private static bool TryReadUncenteredTransform(ReadOnlySpan<char> text, Span<float> numbers)
    {
        return TryReadOperation(ref text, "scale", numbers[..2])
            && TryReadOperation(ref text, "translate", numbers[2..4])
            && TryReadOperation(ref text, "rotate", numbers[4..5]);
    }

    private static bool TryReadOperation(ref ReadOnlySpan<char> text, string name, Span<float> numbers)
    {
        if (!ReadToken(ref text).Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var i = 0; i < numbers.Length; i++)
        {
            var token = ReadToken(ref text);

            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static ReadOnlySpan<char> ReadToken(ref ReadOnlySpan<char> text)
    {
        text = text.TrimStart();
        var length = 0;

        while (length < text.Length && !char.IsWhiteSpace(text[length]))
        {
            length++;
        }

        var token = text[..length];
        text = text[length..];
        return token;
    }

    private int ReadComponents(ReadOnlySpan<char> text, string key, Span<float> components, out bool byteColor)
    {
        text = text.Trim();
        byteColor = !text.IsEmpty && text[0] == '{';

        if (byteColor || (!text.IsEmpty && text[0] == '['))
        {
            var terminated = text[^1] is '}' or ']';
            text = terminated ? text[1..^1] : text[1..];
        }

        var count = 0;

        while (!text.IsEmpty)
        {
            text = text.TrimStart(" \t\r\n,");

            if (text.IsEmpty)
            {
                break;
            }

            var length = text[0] is '+' or '-' ? 1 : 0;
            var digits = 0;

            while (length < text.Length && char.IsAsciiDigit(text[length]))
            {
                length++;
                digits++;
            }

            if (length < text.Length && text[length] == '.')
            {
                length++;

                while (length < text.Length && char.IsAsciiDigit(text[length]))
                {
                    length++;
                    digits++;
                }
            }

            if (digits == 0)
            {
                break;
            }

            if (length < text.Length && text[length] is 'e' or 'E')
            {
                var exponent = length++;

                if (length < text.Length && text[length] is '+' or '-')
                {
                    length++;
                }

                var start = length;

                while (length < text.Length && char.IsAsciiDigit(text[length]))
                {
                    length++;
                }

                if (length == start)
                {
                    length = exponent;
                }
            }

            var token = text[..length];
            text = text[length..];
            var value = ParseFloat(token, key);

            if (count < components.Length)
            {
                components[count] = value;
            }

            count++;
        }

        return count;
    }

    private float ParseFloat(ReadOnlySpan<char> text, string key)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
        {
            throw Invalid(key, $"invalid finite numeric value '{text}'");
        }

        return value;
    }

    private InvalidDataException Invalid(string key, string reason) => new($"{path}: {key}: {reason}.");
}
