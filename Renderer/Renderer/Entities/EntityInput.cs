using System.Globalization;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Marks a method as an entity I/O input handler, the equivalent of Source's <c>DEFINE_INPUTFUNC</c>.
/// </summary>
/// <remarks>
/// The method must be an instance method taking one <see cref="EntityInputData"/> and returning void. A
/// class's own inputs can be private, but declare one protected to pass it down to subclasses, since
/// private methods are not visible on the derived class the table is built for. The name is always spelled
/// out because it is what maps were authored against, not whatever the method happens to be called.
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
/// What comes in with an entity I/O input: the parameter and who sent it. Source's <c>inputdata_t</c>.
/// </summary>
public readonly struct EntityInputData
{
    /// <summary>Gets the parameter passed with the input, if any.</summary>
    public string? Parameter { get; init; }

    /// <summary>Gets the entity that started the I/O chain.</summary>
    public BaseEntity? Activator { get; init; }

    /// <summary>Gets the entity that fired the output.</summary>
    public BaseEntity? Caller { get; init; }

    /// <summary>Reads the parameter as a float, as Source's <c>inputdata.value.Float()</c> does.</summary>
    public float Float(float defaultValue = 0f)
        => float.TryParse(Parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;

    /// <summary>Reads the parameter as an integer.</summary>
    public int Int(int defaultValue = 0)
        => int.TryParse(Parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;

    /// <summary>
    /// Reads the parameter as a vector, three numbers separated by spaces, the way a map authors one.
    /// </summary>
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
