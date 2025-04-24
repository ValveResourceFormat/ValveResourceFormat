using System.Linq;
using System.Reflection;
using ValveResourceFormat.IO;

#nullable disable

namespace ValveResourceFormat;

public static class ResourceTypeExtensions
{
    /// <summary>
    /// Return <see cref="ExtensionAttribute"/> for given <see cref="ResourceType"/>.
    /// </summary>
    /// <param name="value">Resource type.</param>
    /// <returns>Extension type string.</returns>
    public static string GetExtension(this ResourceType value)
    {
        if (value == ResourceType.Unknown)
        {
            return null;
        }

        var intValue = (int)value;
        var field = typeof(ResourceType)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(f => (int)f.GetRawConstantValue() == intValue);

        return field?.GetCustomAttribute<ExtensionAttribute>(inherit: false)?.Extension;
    }

    internal static ResourceType DetermineByFileExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return ResourceType.Unknown;
        }

        extension = extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal) ? extension[1..^2] : extension[1..];

        var fields = typeof(ResourceType).GetFields(BindingFlags.Public | BindingFlags.Static);

        foreach (var field in fields)
        {
            var fieldExtension = field.GetCustomAttribute<ExtensionAttribute>(inherit: false)?.Extension;

            if (fieldExtension == extension)
            {
                return (ResourceType)field.GetValue(null);
            }
        }

        return ResourceType.Unknown;
    }
}
