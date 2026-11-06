using System.Globalization;
using System.IO;

namespace ValveResourceFormat.Source1.Material.Proxies;

internal enum Source1MaterialVarType
{
    Undefined,
    Float,
    Int,
    Vector,
    Matrix,
    String,
}

/// <summary>
/// A variable of a Source 1 material, such as <c>$color</c>, that the proxies of the material read and write.
/// </summary>
internal sealed class Source1MaterialVar
{
    public const int MatrixSize = 16;

    private readonly float[] vector = new float[4];
    private readonly float[] matrix = new float[MatrixSize];
    private int intValue;

    public Source1MaterialVarType Type { get; private set; }

    public int VectorSize { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public ReadOnlySpan<float> Vector => vector.AsSpan(0, VectorSize);

    public ReadOnlySpan<float> Matrix => matrix;

    /// <summary>
    /// The integer and the vector share their storage whatever the type is, so a read does not convert.
    /// A component of a variable that is not a vector is 0.
    /// </summary>
    public float GetFloat(int component = -1)
    {
        if (component >= 0)
        {
            return Type == Source1MaterialVarType.Vector && component < VectorSize ? vector[component] : 0f;
        }

        return vector[0];
    }

    public int GetInt() => intValue;

    /// <summary>Copies the stored components, which are the same number for a float or an integer as for a vector.</summary>
    public void CopyVector(Span<float> destination) => vector.AsSpan(0, destination.Length).CopyTo(destination);

    public void SetFloat(float value)
    {
        Type = Source1MaterialVarType.Float;
        intValue = (int)value;
        Array.Fill(vector, value);
    }

    public void SetInt(int value)
    {
        Type = Source1MaterialVarType.Int;
        intValue = value;
        Array.Fill(vector, (float)value);
    }

    public void SetVector(ReadOnlySpan<float> value)
    {
        Type = Source1MaterialVarType.Vector;
        VectorSize = Math.Min(value.Length, vector.Length);
        value[..VectorSize].CopyTo(vector);
    }

    public void SetVectorComponent(int component, float value)
    {
        if (component < VectorSize)
        {
            vector[component] = value;
        }
    }

    public void FillVector(float value)
    {
        Array.Fill(vector, value, 0, VectorSize);
    }

    public void SetMatrix(ReadOnlySpan<float> value)
    {
        Type = Source1MaterialVarType.Matrix;
        value[..MatrixSize].CopyTo(matrix);
    }

    public void SetString(string value)
    {
        Type = Source1MaterialVarType.String;
        Text = value;
    }

    /// <summary>Returns whether a material variable holds a texture transform, such as <c>$bumptransform</c> or <c>$bumptransform2</c>.</summary>
    public static bool IsTransformName(string name) =>
        name.EndsWith("transform", StringComparison.OrdinalIgnoreCase) || name.EndsWith("transform2", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sets the variable from the text of a VMT parameter. A text that is a whole integer is an integer, any other number is a float.
    /// </summary>
    public void SetFromParameter(string name, string text, VmtParameters parameters)
    {
        var trimmed = text.Trim();

        if (IsTransformName(name))
        {
            try
            {
                SetMatrix(ToMatrix(parameters.Transform(name)));
                return;
            }
            catch (InvalidDataException)
            {
                SetString(text);
                return;
            }
        }

        if (trimmed.StartsWith('[') || trimmed.StartsWith('{') || IsNumberList(trimmed))
        {
            SetVectorFromText(trimmed);
            return;
        }

        if (int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            SetInt(integer);
            return;
        }

        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && float.IsFinite(number))
        {
            SetFloat(number);
            return;
        }

        SetString(text);
    }

    private static bool IsNumberList(string text)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        return true;
    }

    private void SetVectorFromText(string text)
    {
        var byteColor = text[0] == '{';
        var parts = text.Trim('[', ']', '{', '}').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Span<float> components = stackalloc float[4];
        var count = 0;

        foreach (var part in parts)
        {
            if (count == components.Length || !float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var component))
            {
                break;
            }

            components[count++] = byteColor ? component / 255f : component;
        }

        if (count == 0)
        {
            SetString(text);
            return;
        }

        SetVector(components[..count]);
    }

    // The layout of a texture transform is row-major for column vectors, with the translation in the fourth column
    private static float[] ToMatrix(Matrix4x4 transform)
    {
        var matrix = new float[MatrixSize];
        matrix[0] = transform.M11;
        matrix[1] = transform.M21;
        matrix[3] = transform.M41;
        matrix[4] = transform.M12;
        matrix[5] = transform.M22;
        matrix[7] = transform.M42;
        matrix[10] = 1f;
        matrix[15] = 1f;
        return matrix;
    }
}
