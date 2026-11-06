using System.Globalization;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// Hands out names that the model compiler accepts and that differ from every name handed out before, ignoring case.
/// </summary>
internal sealed class ModelNameSet
{
    private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    public string Create(string name)
    {
        var sanitized = ModelNames.Sanitize(name);

        if (sanitized.Length == 0)
        {
            sanitized = "unnamed";
        }

        return MakeUnique(sanitized);
    }

    public string Create(string partName, int partIndex, string suffix)
    {
        var part = ModelNames.Sanitize(partName);
        var name = part + suffix;

        if (part.Length > 0 && names.Add(name))
        {
            return name;
        }

        return MakeUnique(string.Create(CultureInfo.InvariantCulture, $"{part}_part{partIndex}{suffix}"));
    }

    private string MakeUnique(string name)
    {
        while (!names.Add(name))
        {
            name += "_";
        }

        return name;
    }
}
