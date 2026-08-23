using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps;

/// <summary>
/// The user-configured parameter values of a smart prop placed in a VMAP document.
/// </summary>
public sealed record SmartPropMapParameters(string SmartPropFilename, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>
    /// Reads the smart prop filename and user-configured values from a CMapSmartProp element.
    /// </summary>
    public static SmartPropMapParameters? Read(Datamodel.Element element)
    {
        if (!element.TryGetValue("smartPropFilename", out var filenameValue) || filenameValue is not string filename || filename.Length == 0)
        {
            return null;
        }

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetValue("parameters", out var parametersValue) && parametersValue is Datamodel.Element parameters
            && parameters.TryGetValue("values", out var valuesValue) && valuesValue is Datamodel.ElementArray valueEntries)
        {
            for (var i = 0; i < valueEntries.Count; i++)
            {
                if (valueEntries[i] is not Datamodel.Element valueEntry
                    || !valueEntry.TryGetValue("value", out var entryValue) || entryValue is not Datamodel.Element entry
                    || !entry.TryGetValue("parameterName", out var nameValue) || nameValue is not string name || name.Length == 0
                    || !entry.TryGetValue("value", out var parameterValue) || parameterValue is null)
                {
                    continue;
                }

                values[name] = parameterValue.ToString() ?? string.Empty;
            }
        }

        return new SmartPropMapParameters(filename, values);
    }

    /// <summary>
    /// Creates an evaluation context using the smart prop defaults overridden by this map instance.
    /// </summary>
    public SmartPropEvaluationContext CreateEvaluationContext(KVObject smartPropRoot)
    {
        var variables = SmartPropVariableMap.Build(smartPropRoot);
        var definitions = SmartPropVariableMap.ReadVariableDefinitions(smartPropRoot);
        Dictionary<string, object?> overrides = new(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in Values)
        {
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    overrides[name] = SmartPropVariableMap.CoerceFromKV(definition.Type, new KVObject(value));
                    break;
                }
            }
        }

        return new SmartPropEvaluationContext(variables, overrides: overrides);
    }
}
