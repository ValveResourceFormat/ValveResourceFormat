using System.Globalization;
using ValveResourceFormat.ResourceTypes;

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

    /// <summary>Reads the parameter as a float.</summary>
    public float Float(float defaultValue = 0f)
        => float.TryParse(Parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;

    /// <summary>Reads the parameter as an integer.</summary>
    public int Int(int defaultValue = 0)
        => int.TryParse(Parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;

    /// <summary>Reads the parameter as a vector of three space-separated numbers.</summary>
    public Vector3 Vector(Vector3 defaultValue = default)
        => Parameter != null && EntityTransformHelper.TryParseVector3(Parameter, out var value)
            ? value
            : defaultValue;

    /// <summary>Reads the parameter as a boolean, accepting both <c>1</c> and <c>true</c>.</summary>
    public bool Bool(bool defaultValue = false)
    {
        if (bool.TryParse(Parameter, out var value))
        {
            return value;
        }

        return int.TryParse(Parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number != 0
            : defaultValue;
    }
}
