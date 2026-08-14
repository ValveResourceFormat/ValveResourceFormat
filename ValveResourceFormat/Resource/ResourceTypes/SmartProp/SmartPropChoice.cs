using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// An option within a smart prop choice, defining a selectable variant and the
    /// variable values it applies when active.
    /// </summary>
    /// <param name="Name">Option identifier name.</param>
    /// <param name="DisplayName">User-facing display name, or Name if empty.</param>
    /// <param name="VariableValues">Dictionary of variable name to override value.</param>
    public sealed record SmartPropChoiceOption(
        string Name,
        string DisplayName,
        IReadOnlyDictionary<string, object?> VariableValues);

    /// <summary>
    /// A choice parameter on a smart prop root, containing selectable options that
    /// modify variables when chosen.
    /// </summary>
    /// <param name="Name">The choice name.</param>
    /// <param name="DefaultOption">The name of the default active option.</param>
    /// <param name="Options">The list of available options.</param>
    public sealed record SmartPropChoice(
        string Name,
        string DefaultOption,
        IReadOnlyList<SmartPropChoiceOption> Options);

    /// <summary>
    /// Parses smart prop choices from a CSmartPropRoot's m_Choices array and applies option overrides.
    /// </summary>
    public static class SmartPropChoiceMap
    {
        /// <summary>
        /// Reads m_Choices from a CSmartPropRoot and returns a list of choices with their options.
        /// </summary>
        /// <param name="root">The root smart prop KVObject.</param>
        /// <returns>List of parsed choices.</returns>
        public static List<SmartPropChoice> ReadChoices(KVObject? root)
        {
            List<SmartPropChoice> choices = [];
            if (root is null || !root.TryGetValue("m_Choices", out var choicesNode) || !choicesNode.IsArray)
            {
                return choices;
            }

            var span = choicesNode.AsArraySpan();
            for (var i = 0; i < span.Length; i++)
            {
                var choiceEntry = span[i];
                if (choiceEntry.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var name = choiceEntry.TryGetValue("m_Name", out var nameNode) && nameNode.ValueType == KVValueType.String
                    ? (string)nameNode
                    : string.Empty;

                if (name.Length == 0)
                {
                    continue;
                }

                var defaultOption = choiceEntry.TryGetValue("m_DefaultOption", out var defaultOptNode) && defaultOptNode.ValueType == KVValueType.String
                    ? (string)defaultOptNode
                    : string.Empty;

                List<SmartPropChoiceOption> options = [];
                if (choiceEntry.TryGetValue("m_Options", out var optionsNode) && optionsNode.IsArray)
                {
                    var optSpan = optionsNode.AsArraySpan();
                    for (var j = 0; j < optSpan.Length; j++)
                    {
                        var optEntry = optSpan[j];
                        if (optEntry.ValueType != KVValueType.Collection)
                        {
                            continue;
                        }

                        var optName = optEntry.TryGetValue("m_Name", out var optNameNode) && optNameNode.ValueType == KVValueType.String
                            ? (string)optNameNode
                            : string.Empty;

                        var displayName = optEntry.TryGetValue("m_DisplayName", out var dispNameNode) && dispNameNode.ValueType == KVValueType.String
                            ? (string)dispNameNode
                            : string.Empty;

                        if (displayName.Length == 0)
                        {
                            displayName = optName;
                        }

                        var variableValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        if (optEntry.TryGetValue("m_VariableValues", out var varValsNode) && varValsNode.IsArray)
                        {
                            var valSpan = varValsNode.AsArraySpan();
                            for (var k = 0; k < valSpan.Length; k++)
                            {
                                var valEntry = valSpan[k];
                                if (valEntry.ValueType != KVValueType.Collection)
                                {
                                    continue;
                                }

                                ParseVariableValue(valEntry, variableValues);
                            }
                        }

                        options.Add(new SmartPropChoiceOption(optName, displayName, variableValues));
                    }
                }

                choices.Add(new SmartPropChoice(name, defaultOption, options));
            }

            return choices;
        }

        private static void ParseVariableValue(KVObject entry, Dictionary<string, object?> target)
        {
            string? targetName = null;
            if (entry.TryGetValue("m_TargetName", out var targetNode) && targetNode.ValueType == KVValueType.String)
            {
                targetName = (string)targetNode;
            }
            else if (entry.TryGetValue("m_VariableName", out var varNameNode) && varNameNode.ValueType == KVValueType.String)
            {
                targetName = (string)varNameNode;
            }

            if (string.IsNullOrEmpty(targetName))
            {
                if (entry.TryGetValue("m_VariableValue", out var varValNode) && varValNode.ValueType == KVValueType.Collection)
                {
                    ParseVariableValue(varValNode, target);
                }

                return;
            }

            var dataType = entry.TryGetValue("m_DataType", out var dtNode) && dtNode.ValueType == KVValueType.String
                ? (string)dtNode
                : null;

            if (entry.TryGetValue("m_Value", out var valNode))
            {
                target[targetName] = SmartPropVariableMap.CoerceFromKV(dataType, valNode);
            }
            else if (entry.TryGetValue("m_VariableValue", out var varValNode))
            {
                target[targetName] = SmartPropVariableMap.CoerceFromKV(dataType, varValNode);
            }
        }

        /// <summary>
        /// Applies the chosen option's variable overrides onto a variable dictionary.
        /// </summary>
        /// <param name="variables">The variable map to update.</param>
        /// <param name="choice">The choice definition.</param>
        /// <param name="selectedOptionName">The selected option name, or null to use default.</param>
        public static void ApplyChoice(Dictionary<string, object?> variables, SmartPropChoice choice, string? selectedOptionName)
        {
            var option = FindOption(choice.Options, selectedOptionName)
                ?? FindOption(choice.Options, choice.DefaultOption)
                ?? (choice.Options.Count > 0 ? choice.Options[0] : null);

            if (option != null)
            {
                foreach (var (varName, value) in option.VariableValues)
                {
                    variables[varName] = value;
                }
            }
        }

        private static SmartPropChoiceOption? FindOption(IReadOnlyList<SmartPropChoiceOption> options, string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return options[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Applies all choices onto a variable dictionary.
        /// </summary>
        /// <param name="variables">The variable map to update.</param>
        /// <param name="choices">The list of choices.</param>
        /// <param name="selectedOptions">Optional dictionary mapping choice name to selected option name.</param>
        public static void ApplyChoices(Dictionary<string, object?> variables, IReadOnlyList<SmartPropChoice> choices, IReadOnlyDictionary<string, string>? selectedOptions = null)
        {
            foreach (var choice in choices)
            {
                var selected = selectedOptions != null && selectedOptions.TryGetValue(choice.Name, out var opt)
                    ? opt
                    : choice.DefaultOption;

                ApplyChoice(variables, choice, selected);
            }
        }
    }
}
