namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Organizes shader parameters into UI groups and sections.
/// </summary>
public struct UiGroup
{
    /// <summary>Gets the heading name.</summary>
    public string Heading { get; private set; }
    /// <summary>Gets the heading sort order.</summary>
    public int HeadingOrder { get; private set; }
    /// <summary>Gets the group name.</summary>
    public string Group { get; private set; }
    /// <summary>Gets the group sort order.</summary>
    public int GroupOrder { get; private set; }
    /// <summary>Gets the variable sort order.</summary>
    public int VariableOrder { get; private set; }

    /// <summary>Gets the compact string representation.</summary>
    public string CompactString { get; private init; }

    /// <summary>
    /// Initializes a new instance with empty values.
    /// </summary>
    public UiGroup() : this(string.Empty) { }

    /// <summary>
    /// Initializes a new instance with the specified values.
    /// </summary>
    public UiGroup(string heading,
                   int headingOrder = 0,
                   string group = "",
                   int groupOrder = 0,
                   int variableOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(group);
        Heading = heading;
        HeadingOrder = headingOrder;
        Group = group;
        GroupOrder = groupOrder;
        VariableOrder = variableOrder;

        CompactString = $"{heading},{headingOrder}/{group},{groupOrder}/{variableOrder}";
    }

    /// <summary>
    /// Creates a UiGroup from a compact string representation.
    /// </summary>
    public static UiGroup FromCompactString(string compactString)
    {
        var uiGroup = new UiGroup()
        {
            CompactString = compactString,
        };

        if (compactString.Length == 0)
        {
            return uiGroup;
        }

        var bySlash = compactString.Split("/", 3, StringSplitOptions.TrimEntries);

        if (bySlash.Length > 0)
        {
            (uiGroup.Heading, uiGroup.HeadingOrder) = SplitNameOrder(bySlash[0]);
        }

        if (bySlash.Length > 1)
        {
            if (bySlash.Length > 2)
            {
                (uiGroup.Group, uiGroup.GroupOrder) = SplitNameOrder(bySlash[1]);
                (_, uiGroup.VariableOrder) = SplitNameOrder(bySlash[2]);
            }
            else
            {
                (_, uiGroup.VariableOrder) = SplitNameOrder(bySlash[1]);
            }
        }

        return uiGroup;
    }

    private static (string, int) SplitNameOrder(string bySlash)
    {
        var name = string.Empty;
        var order = 0;
        var byComma = bySlash.Split(",", 2, StringSplitOptions.TrimEntries);
        for (var j = 0; j < byComma.Length; j++)
        {
            if (!int.TryParse(byComma[j], out order))
            {
                name = byComma[j];
            }
        }

        return (name, order);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the compact string representation of the UI group.
    /// </remarks>
    public readonly override string ToString()
    {
        return CompactString;
    }
}
