using System.Globalization;
using System.IO;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Typed Source material values with case-insensitive keys.
/// </summary>
internal readonly struct VmtParameters(IReadOnlyDictionary<string, string> values, string path)
{
    private enum TransformOperation
    {
        Invalid,
        Center,
        Scale,
        Translate,
        Rotate,
    }

    public IReadOnlyDictionary<string, string> All => values;

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
                throw Invalid(key, "expected one to four scalar-conversion components");
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
            throw Invalid(key, "expected one to four components");
        }

        var scalar = count == 1 && !text.AsSpan().TrimStart().StartsWith("[", StringComparison.Ordinal) && !byteColor;
        var value = scalar ? new Vector3(components[0]) : new Vector3(components[0], components[1], components[2]);

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
            throw Invalid(key, "expected two to four components");
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

        var remaining = text.AsSpan();

        if (remaining.IsWhiteSpace())
        {
            throw Invalid(key, "empty texture transform");
        }

        Vector2? center = null;
        var scale = Vector2.One;
        var translation = Vector2.Zero;
        var rotation = 0.0f;

        while (!remaining.IsWhiteSpace())
        {
            var name = ReadToken(ref remaining);
            var operation = GetOperation(name);
            var xToken = ReadToken(ref remaining);
            var yToken = operation == TransformOperation.Rotate ? default : ReadToken(ref remaining);

            if (operation == TransformOperation.Invalid || xToken.IsEmpty || (operation != TransformOperation.Rotate && yToken.IsEmpty))
            {
                throw Invalid(key, $"invalid texture transform operation '{name.ToString().ToLowerInvariant()}'");
            }

            var x = ParseFloat(xToken, key);
            var y = operation == TransformOperation.Rotate ? 0 : ParseFloat(yToken, key);

            switch (operation)
            {
                case TransformOperation.Center:
                    center = new Vector2(x, y);
                    break;
                case TransformOperation.Scale:
                    scale = new Vector2(x, y);
                    break;
                case TransformOperation.Translate:
                    translation = new Vector2(x, y);
                    break;
                case TransformOperation.Rotate:
                    rotation = x;
                    break;
            }
        }

        var scaleMatrix = Matrix4x4.CreateScale(new Vector3(scale.X, scale.Y, 1));
        var rotationMatrix = Matrix4x4.CreateRotationZ(rotation * (MathF.PI / 180f));
        Matrix4x4 matrix;

        if (center is { } origin)
        {
            matrix = Matrix4x4.CreateTranslation(new Vector3(-origin.X, -origin.Y, 0))
                * scaleMatrix
                * rotationMatrix
                * Matrix4x4.CreateTranslation(new Vector3(origin.X + translation.X, origin.Y + translation.Y, 0));
        }
        else
        {
            // Without a center the rotation comes first, then the scale, about the origin of the texture
            matrix = rotationMatrix
                * scaleMatrix
                * Matrix4x4.CreateTranslation(new Vector3(translation.X, translation.Y, 0));
        }

        if (!float.IsFinite(matrix.M11) || !float.IsFinite(matrix.M12)
            || !float.IsFinite(matrix.M21) || !float.IsFinite(matrix.M22)
            || !float.IsFinite(matrix.M41) || !float.IsFinite(matrix.M42))
        {
            throw Invalid(key, "texture transform overflows");
        }

        return matrix;
    }

    private static TransformOperation GetOperation(ReadOnlySpan<char> name)
    {
        if (name.Equals("center", StringComparison.OrdinalIgnoreCase))
        {
            return TransformOperation.Center;
        }

        if (name.Equals("scale", StringComparison.OrdinalIgnoreCase))
        {
            return TransformOperation.Scale;
        }

        if (name.Equals("translate", StringComparison.OrdinalIgnoreCase))
        {
            return TransformOperation.Translate;
        }

        if (name.Equals("rotate", StringComparison.OrdinalIgnoreCase))
        {
            return TransformOperation.Rotate;
        }

        return TransformOperation.Invalid;
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
                throw Invalid(key, $"invalid finite numeric value '{text}'");
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
