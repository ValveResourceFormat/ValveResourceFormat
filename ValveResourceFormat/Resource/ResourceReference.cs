using System.IO;

namespace ValveResourceFormat;

/// <summary>
/// The places in a resource a reference to another file can be stored in.
/// </summary>
[Flags]
public enum ResourceReferenceKind
{
    /// <summary>
    /// No source.
    /// </summary>
    None = 0,

    /// <summary>
    /// Listed in the external reference list ("RERL") block.
    /// </summary>
    External = 1,

    /// <summary>
    /// A child resource the compiler generated while compiling this one.
    /// </summary>
    Child = 2,

    /// <summary>
    /// A weak reference, which the game only loads when it needs it.
    /// </summary>
    Weak = 4,

    /// <summary>
    /// An additional related file the compiler recorded.
    /// </summary>
    Related = 8,

    /// <summary>
    /// A content file this resource was compiled from.
    /// </summary>
    InputDependency = 16,

    /// <summary>
    /// A named subasset of another resource, such as a sound event.
    /// </summary>
    Subasset = 32,

    /// <summary>
    /// A file name stored in the resource data.
    /// </summary>
    Data = 64,

    /// <summary>
    /// An image listed in a panorama file's image table.
    /// </summary>
    PanoramaImage = 128,

    /// <summary>
    /// An entry of a resource manifest.
    /// </summary>
    Manifest = 256,
}

/// <summary>
/// A reference from one resource to another file.
/// </summary>
/// <param name="Name">The referenced file name as the resource stores it.</param>
/// <param name="Kinds">Every place in the resource this name was found in.</param>
/// <param name="Type">The resource type the name resolves to, or <see cref="ResourceType.Unknown"/> when it names something else.</param>
/// <param name="Source">The data member the name was read from, or <c>null</c> when it came from a whole list.</param>
/// <param name="Id">The id the external reference list stores for this name, or zero.</param>
public readonly record struct ResourceReference(
    string Name,
    ResourceReferenceKind Kinds,
    ResourceType Type,
    string? Source,
    ulong Id)
{
    /// <summary>
    /// Creates a reference, determining <see cref="Type"/> from the file extension of <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The referenced file name.</param>
    /// <param name="kinds">Where the name was found.</param>
    /// <param name="source">The data member the name was read from.</param>
    /// <param name="id">The id the external reference list stores for this name.</param>
    /// <returns>The reference.</returns>
    public static ResourceReference Create(string name, ResourceReferenceKind kinds, string? source = null, ulong id = 0)
    {
        ArgumentNullException.ThrowIfNull(name);

        var type = ResourceTypeExtensions.DetermineByFileExtension(Path.GetExtension(name.AsSpan()));

        return new ResourceReference(name, kinds, type, source, id);
    }
}
