namespace ValveResourceFormat.SourceGenerators;

/// <summary>
/// The Hungarian notation name a property has in KeyValues3 data. Shared with the schema converter, which only writes
/// <c>[KVProperty]</c> on properties this rule does not name correctly.
/// </summary>
internal static class HungarianPropertyName
{
    /// <summary>
    /// The key a property is read from without <c>[KVProperty]</c>: <c>m_</c>, a type prefix (<c>b</c> for
    /// bools, <c>fl</c> for floating point, <c>n</c> for integers) and the property name, which starts
    /// lowercase after an empty prefix unless it begins with an acronym.
    /// </summary>
    /// <param name="propertyName">The name of the property.</param>
    /// <param name="typeKeyword">The type of the property as written in C#, such as <c>float</c>.</param>
    public static string Get(string propertyName, string typeKeyword)
    {
        var prefix = typeKeyword switch
        {
            "bool" => "b",
            "float" or "double" => "fl",
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" => "n",
            _ => string.Empty,
        };

        if (prefix.Length > 0 || propertyName.Length == 0 || (propertyName.Length > 1 && char.IsUpper(propertyName[1])))
        {
            return "m_" + prefix + propertyName;
        }

        return "m_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }
}
