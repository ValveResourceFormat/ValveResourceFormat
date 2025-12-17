using System.Collections.Frozen;
using System.Reflection;
using ValveResourceFormat.IO;

namespace ValveResourceFormat;

/// <summary>
/// Extension methods for <see cref="ResourceType"/>.
/// </summary>
public static class ResourceTypeExtensions
{
    private static readonly FrozenDictionary<ResourceType, string> ResourceTypeToExtension;
    private static readonly FrozenDictionary<string, ResourceType>.AlternateLookup<ReadOnlySpan<char>> ExtensionCharsToResourceType;

#pragma warning disable CA1810 // Initialize static fields inline
    static ResourceTypeExtensions()
#pragma warning restore CA1810
    {
        var typeToExtension = new Dictionary<ResourceType, string>();
        var extensionToType = new Dictionary<string, ResourceType>(StringComparer.Ordinal);

        var fields = typeof(ResourceType).GetFields(BindingFlags.Public | BindingFlags.Static);

        foreach (var field in fields)
        {
            var extension = field.GetCustomAttribute<ExtensionAttribute>(inherit: false)?.Extension;

            if (extension != null && field.GetValue(null) is ResourceType resourceType)
            {
                typeToExtension[resourceType] = extension;
                extensionToType[extension] = resourceType;
            }
        }

        ResourceTypeToExtension = typeToExtension.ToFrozenDictionary();
        ExtensionCharsToResourceType = extensionToType
            .ToFrozenDictionary(StringComparer.Ordinal)
            .GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>
    /// Return <see cref="ExtensionAttribute"/> for given <see cref="ResourceType"/>.
    /// </summary>
    /// <param name="value">Resource type.</param>
    /// <returns>Extension type string.</returns>
    public static string? GetExtension(this ResourceType value)
    {
        if (value == ResourceType.Unknown)
        {
            return null;
        }

        return ResourceTypeToExtension.TryGetValue(value, out var extension) ? extension : null;
    }

    internal static ResourceType DetermineByFileExtension(ReadOnlySpan<char> extension)
    {
        if (extension.IsEmpty)
        {
            return ResourceType.Unknown;
        }

        extension = extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal) ? extension[1..^2] : extension[1..];

        return ExtensionCharsToResourceType.TryGetValue(extension, out var resourceType)
            ? resourceType
            : ResourceType.Unknown;
    }
}
