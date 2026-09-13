namespace ValveResourceFormat.IO;

/// <summary>
/// Represents a combination of surface property and collision tags.
/// </summary>
public sealed record SurfaceTagCombo(string SurfacePropName, HashSet<string> InteractAsStrings)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SurfaceTagCombo"/> record.
    /// </summary>
    public SurfaceTagCombo(string surfacePropName, string[] collisionTags)
        : this(surfacePropName, new HashSet<string>(collisionTags))
    { }

    /// <summary>
    /// Gets the string representation of the material.
    /// </summary>
    public string StringMaterial => string.Join('+', InteractAsStrings) + '$' + SurfacePropName;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the hash code of the string material representation.
    /// </remarks>
    public override int GetHashCode() => StringMaterial.GetHashCode(StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether the specified <see cref="SurfaceTagCombo"/> is equal to the current instance.
    /// </summary>
    public bool Equals(SurfaceTagCombo? other) => other is not null && GetHashCode() == other.GetHashCode();
}
