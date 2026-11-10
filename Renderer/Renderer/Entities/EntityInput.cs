using System.Globalization;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Marks a method as an entity I/O input handler.
/// </summary>
/// <remarks>
/// The method must be an instance method taking one <see cref="EntityInputData"/> and returning void.
/// Declare it protected to pass it down to subclasses, since the table is built per derived class and
/// cannot see private base methods. The name is spelled out because maps are authored against it, not
/// the method name.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EntityInputAttribute : Attribute
{
    /// <summary>Gets the input's name as authored in maps.</summary>
    public string Name { get; }

    /// <summary>Names the input.</summary>
    public EntityInputAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// The parameter and senders of an entity I/O input.
/// </summary>
public readonly struct EntityInputData
{
    /// <summary>Gets the parameter passed with the input, if any.</summary>
    public string? Parameter { get; init; }

    /// <summary>Gets the entity that started the I/O chain.</summary>
    public BaseEntity? Activator { get; init; }

    /// <summary>Gets the entity that fired the output.</summary>
    public BaseEntity? Caller { get; init; }

    /// <summary>
    /// Formats a float the way the game turns one into a string parameter: six decimals, with the trailing
    /// zeros stripped down to one, so 1 becomes <c>1.0</c> and 0.25 becomes <c>0.25</c>.
    /// </summary>
    public static string FormatFloat(float value)
    {
        // Not "0.0#####": custom formats round a float to 7 significant digits, and ties away from zero
        var text = value.ToString("F6", CultureInfo.InvariantCulture).TrimEnd('0');

        return text.EndsWith('.', StringComparison.Ordinal) ? text + "0" : text;
    }

    /// <summary>Reads the number the parameter starts with, which is zero for text that is not a number.</summary>
    /// <param name="defaultValue">The value returned when there is no parameter.</param>
    public float Float(float defaultValue = 0f)
        => string.IsNullOrEmpty(Parameter) ? defaultValue : ValueParsing.ParseLeadingFloat(Parameter);

    /// <summary>Reads the integer the parameter starts with, which is zero for text that is not a number.</summary>
    /// <param name="defaultValue">The value returned when there is no parameter.</param>
    public int Int(int defaultValue = 0)
        => string.IsNullOrEmpty(Parameter) ? defaultValue : ValueParsing.ParseLeadingInt(Parameter);

    /// <summary>Reads up to three space separated numbers from the parameter, with the missing ones zero.</summary>
    /// <param name="defaultValue">The value returned when there is no parameter.</param>
    public Vector3 Vector(Vector3 defaultValue = default)
    {
        if (string.IsNullOrEmpty(Parameter))
        {
            return defaultValue;
        }

        Span<float> components = stackalloc float[3];
        ValueParsing.ParseVector(Parameter, components);

        return new Vector3(components);
    }

    /// <summary>Reads the parameter as an "R G B" or "R G B A" color, which is its red, green and blue as 0-255.</summary>
    /// <returns>The color, or null when the parameter is not three or four numbers.</returns>
    public Vector3? Color()
    {
        if (!ValueParsing.TryParseColor(Parameter, out var color))
        {
            return null;
        }

        return new Vector3(color.R, color.G, color.B);
    }

    /// <summary>Reads the parameter as a boolean, where an empty parameter is false.</summary>
    /// <param name="defaultValue">The value returned when there is no parameter or it is not a boolean.</param>
    public bool Bool(bool defaultValue = false)
    {
        if (Parameter == null)
        {
            return defaultValue;
        }

        return ValueParsing.ParseBool(Parameter, defaultValue);
    }
}
