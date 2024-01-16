using System;
using System.Linq;
using System.Reflection;

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
}
