using System.Globalization;
using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps;

/// <summary>
/// The user-configured parameter values of a smart prop placed in a VMAP document.
/// </summary>
public sealed record SmartPropMapParameters(string SmartPropFilename, IReadOnlyDictionary<string, string> Values)
{
    private sealed record WidgetConfiguration(string Name, Vector3 DeltaMin, Vector3 DeltaMax, float DeltaValue);

    private Dictionary<int, List<WidgetConfiguration>> WidgetConfigurations { get; init; } = [];

    /// <summary>Gets the deterministic random seed stored on the placed smart prop.</summary>
    public int RandomSeed { get; init; }

    /// <summary>Gets configured PickOne element ids mapped to their selected child element ids.</summary>
    public IReadOnlyDictionary<int, int> ChoiceElementIds { get; init; } = new Dictionary<int, int>();

    /// <summary>
    /// Reads all placed smart props below a VMAP root element.
    /// </summary>
    public static IReadOnlyList<SmartPropMapParameters> ReadAll(Datamodel.Element mapRoot)
    {
        List<SmartPropMapParameters> smartProps = [];
        if (mapRoot.TryGetValue("world", out var worldValue) && worldValue is Datamodel.Element world)
        {
            ReadAll(world, smartProps);
        }

        return smartProps;
    }

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
        var nodeData = element.TryGetValue("nodeData", out var nodeDataValue) && nodeDataValue is Datamodel.Element data
            ? data
            : null;
        var parameters = element.TryGetValue("parameters", out var parametersValue) && parametersValue is Datamodel.Element directParameters
            ? directParameters
            : nodeData != null && nodeData.TryGetValue("parameters", out parametersValue) && parametersValue is Datamodel.Element nestedParameters
                ? nestedParameters
                : null;

        if (parameters != null
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

        Dictionary<int, int> choiceElementIds = [];
        Dictionary<int, List<WidgetConfiguration>> widgetConfigurations = [];
        if (nodeData != null && nodeData.TryGetValue("configuration", out var configurationValue)
            && configurationValue is Datamodel.ElementArray configuration)
        {
            for (var i = 0; i < configuration.Count; i++)
            {
                if (configuration[i] is not Datamodel.Element wrapper
                    || !wrapper.TryGetValue("value", out var entryValue) || entryValue is not Datamodel.Element entry
                    || !entry.TryGetValue("elementPath", out var pathValue) || pathValue is not Datamodel.IntArray path || path.Count == 0
                    || !entry.TryGetValue("choiceValue", out var choiceValue) || choiceValue is not int choice || choice == int.MinValue)
                {
                    continue;
                }

                var elementId = path[^1];
                choiceElementIds[elementId] = choice;
            }

            for (var i = 0; i < configuration.Count; i++)
            {
                if (configuration[i] is not Datamodel.Element wrapper
                    || !wrapper.TryGetValue("value", out var entryValue) || entryValue is not Datamodel.Element entry
                    || !entry.TryGetValue("elementPath", out var pathValue) || pathValue is not Datamodel.IntArray path || path.Count == 0
                    || !entry.TryGetValue("m_LocatorConfig", out var locatorValue) || locatorValue is not Datamodel.ElementArray locators)
                {
                    continue;
                }

                var elementId = path[^1];
                for (var locatorIndex = 0; locatorIndex < locators.Count; locatorIndex++)
                {
                    if (locators[locatorIndex] is not Datamodel.Element locatorWrapper
                        || !locatorWrapper.TryGetValue("value", out var configValue) || configValue is not Datamodel.Element config)
                    {
                        continue;
                    }

                    var name = config.TryGetValue("m_LocatorName", out var nameValue) && nameValue is string locatorName
                        ? locatorName
                        : string.Empty;
                    var deltaMin = ReadVector(config, "m_vDeltaMin");
                    var deltaMax = ReadVector(config, "m_vDeltaMax");
                    var deltaValue = config.TryGetValue("m_flDeltaValue", out var deltaValueObject) && deltaValueObject is IConvertible convertible
                        ? convertible.ToSingle(CultureInfo.InvariantCulture)
                        : 0f;

                    if (!widgetConfigurations.TryGetValue(elementId, out var elementConfigurations))
                    {
                        elementConfigurations = [];
                        widgetConfigurations[elementId] = elementConfigurations;
                    }

                    elementConfigurations.Add(new WidgetConfiguration(name, deltaMin, deltaMax, deltaValue));
                }
            }
        }

        var randomSeed = element.TryGetValue("randomSeed", out var seedValue) && seedValue is int seed ? seed : 0;
        return new SmartPropMapParameters(filename, values)
        {
            RandomSeed = randomSeed,
            ChoiceElementIds = choiceElementIds,
            WidgetConfigurations = widgetConfigurations,
        };
    }

    private static void ReadAll(Datamodel.Element element, List<SmartPropMapParameters> smartProps)
    {
        var parameters = Read(element);
        if (parameters != null)
        {
            smartProps.Add(parameters);
        }

        if (!element.TryGetValue("children", out var childrenValue) || childrenValue is not Datamodel.ElementArray children)
        {
            return;
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is Datamodel.Element child)
            {
                ReadAll(child, smartProps);
            }
        }
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

        Dictionary<int, int> pickOneSelections = [];
        ResolvePickOneSelections(smartPropRoot, pickOneSelections);
        Dictionary<string, float> widgetOutputValues = new(StringComparer.OrdinalIgnoreCase);
        var initialContext = new SmartPropEvaluationContext(variables, seed: RandomSeed, overrides: overrides);
        ResolveWidgetConfigurations(smartPropRoot, initialContext, widgetOutputValues);
        foreach (var (name, value) in widgetOutputValues)
        {
            var definition = definitions.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            overrides[name] = definition?.Type == "Int" ? (int)MathF.Round(value) : value;
        }

        return new SmartPropEvaluationContext(
            variables,
            seed: RandomSeed,
            overrides: overrides,
            pickOneSelections: pickOneSelections,
            widgetOutputValues: widgetOutputValues);
    }

    private void ResolveWidgetConfigurations(
        KVObject element,
        SmartPropEvaluationContext context,
        Dictionary<string, float> widgetOutputValues)
    {
        if (element.ValueType != KVValueType.Collection)
        {
            return;
        }

        if (element.TryGetValue("m_nElementID", out var idValue)
            && idValue.ValueType != KVValueType.Null
            && WidgetConfigurations.TryGetValue((int)idValue, out var configurations)
            && element.TryGetValue("m_Modifiers", out var modifiers) && modifiers.IsArray)
        {
            foreach (var modifier in modifiers.AsArraySpan())
            {
                ApplyWidgetConfiguration(modifier, configurations, context, widgetOutputValues);
            }
        }

        if (element.TryGetValue("m_Children", out var children) && children.IsArray)
        {
            foreach (var child in children.AsArraySpan())
            {
                ResolveWidgetConfigurations(child, context, widgetOutputValues);
            }
        }
    }

    private static void ApplyWidgetConfiguration(
        KVObject modifier,
        IReadOnlyList<WidgetConfiguration> configurations,
        SmartPropEvaluationContext context,
        Dictionary<string, float> widgetOutputValues)
    {
        var className = SmartPropModifierEvaluator.GetClassName(modifier);
        if (className is not ("CreateSizer" or "CreateRotator"))
        {
            return;
        }

        var name = modifier.TryGetValue("m_Name", out var nameValue) && nameValue.ValueType == KVValueType.String
            ? (string)nameValue
            : string.Empty;
        var configuration = configurations.FirstOrDefault(config => string.Equals(config.Name, name, StringComparison.OrdinalIgnoreCase));
        if (configuration == null)
        {
            return;
        }

        if (className == "CreateRotator")
        {
            SetWidgetOutput(
                modifier,
                "m_OutputVariable",
                context.ResolveScalar(GetValue(modifier, "m_flInitialAngle")) + configuration.DeltaValue,
                widgetOutputValues);
            return;
        }

        SetSizerOutput(modifier, "m_OutputVariableMinX", "m_flInitialMinX", configuration.DeltaMin.X, context, widgetOutputValues);
        SetSizerOutput(modifier, "m_OutputVariableMaxX", "m_flInitialMaxX", configuration.DeltaMax.X, context, widgetOutputValues);
        SetSizerOutput(modifier, "m_OutputVariableMinY", "m_flInitialMinY", configuration.DeltaMin.Y, context, widgetOutputValues);
        SetSizerOutput(modifier, "m_OutputVariableMaxY", "m_flInitialMaxY", configuration.DeltaMax.Y, context, widgetOutputValues);
        SetSizerOutput(modifier, "m_OutputVariableMinZ", "m_flInitialMinZ", configuration.DeltaMin.Z, context, widgetOutputValues);
        SetSizerOutput(modifier, "m_OutputVariableMaxZ", "m_flInitialMaxZ", configuration.DeltaMax.Z, context, widgetOutputValues);
    }

    private static void SetSizerOutput(
        KVObject modifier,
        string outputName,
        string initialName,
        float delta,
        SmartPropEvaluationContext context,
        Dictionary<string, float> widgetOutputValues)
        => SetWidgetOutput(modifier, outputName, context.ResolveScalar(GetValue(modifier, initialName)) + delta, widgetOutputValues);

    private static void SetWidgetOutput(
        KVObject modifier,
        string outputName,
        float value,
        Dictionary<string, float> widgetOutputValues)
    {
        if (modifier.TryGetValue(outputName, out var outputValue)
            && outputValue.ValueType == KVValueType.String
            && (string)outputValue is { Length: > 0 } variable)
        {
            widgetOutputValues[variable] = value;
        }
    }

    private static KVObject? GetValue(KVObject element, string name)
        => element.TryGetValue(name, out var value) ? value : null;

    private static Vector3 ReadVector(Datamodel.Element element, string name)
    {
        if (!element.TryGetValue(name, out var value))
        {
            return Vector3.Zero;
        }

        return value switch
        {
            Vector3 vector => vector,
            Datamodel.FloatArray { Count: >= 3 } array => new Vector3(array[0], array[1], array[2]),
            _ => Vector3.Zero,
        };
    }

    private void ResolvePickOneSelections(KVObject element, Dictionary<int, int> selections)
    {
        if (element.ValueType != KVValueType.Collection)
        {
            return;
        }

        if (element.TryGetValue("m_nElementID", out var idValue) && idValue.ValueType != KVValueType.Null)
        {
            var elementId = (int)idValue;
            if (ChoiceElementIds.TryGetValue(elementId, out var selectedChildId)
                && element.TryGetValue("m_Children", out var children) && children.IsArray)
            {
                var childSpan = children.AsArraySpan();
                for (var i = 0; i < childSpan.Length; i++)
                {
                    if (childSpan[i].TryGetValue("m_nElementID", out var childIdValue) && (int)childIdValue == selectedChildId)
                    {
                        selections[elementId] = i;
                        break;
                    }
                }
            }
        }

        if (!element.TryGetValue("m_Children", out var nestedChildren) || !nestedChildren.IsArray)
        {
            return;
        }

        foreach (var child in nestedChildren.AsArraySpan())
        {
            ResolvePickOneSelections(child, selections);
        }
    }
}
