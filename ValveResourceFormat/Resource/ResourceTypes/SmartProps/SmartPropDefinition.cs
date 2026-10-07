using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps.Elements;
using ValveResourceFormat.ResourceTypes.SmartProps.Modifiers;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// A variable declared by a smart prop.
    /// </summary>
    public sealed class SmartPropVariableDefinition
    {
        /// <summary>Variable name, unique case-insensitively.</summary>
        public required string Name { get; init; }

        /// <summary>Schema class, e.g. <c>CSmartPropVariable_Float</c>.</summary>
        public required string ClassName { get; init; }

        /// <summary>Element id, the key of parameter overrides.</summary>
        public int ElementId { get; init; }

        /// <summary>Value type.</summary>
        public SmartPropVariableType Type { get; init; }

        /// <summary>Default value.</summary>
        public SmartPropValue DefaultValue { get; init; }

        /// <summary>Whether the variable is an exposed parameter.</summary>
        public bool ExposeAsParameter { get; init; }

        /// <summary>Display name, empty when not authored.</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>Expression that hides the parameter in an editor.</summary>
        public string? HideExpression { get; init; }

        /// <summary>Expression that makes the parameter read-only in an editor.</summary>
        public string? ReadOnlyExpression { get; init; }

        /// <summary>Enumerator names of an enum variable, null otherwise.</summary>
        public IReadOnlyList<string>? EnumNames { get; init; }

        /// <summary>Authored minimum for numeric parameters.</summary>
        public double? MinValue { get; init; }

        /// <summary>Authored maximum for numeric parameters.</summary>
        public double? MaxValue { get; init; }

        /// <summary>Model whose material groups a material group variable chooses from.</summary>
        public string? ModelName { get; init; }
    }

    /// <summary>
    /// A named set of variable values, one option of which is applied at the start of an evaluation.
    /// </summary>
    public sealed class SmartPropChoice
    {
        /// <summary>Element id, the key of choice overrides.</summary>
        public int ElementId { get; init; }

        /// <summary>Choice name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Name of the option applied without an override.</summary>
        public string DefaultOption { get; init; } = string.Empty;

        /// <summary>Options.</summary>
        public List<SmartPropChoiceOption> Options { get; } = [];
    }

    /// <summary>
    /// One option of a <see cref="SmartPropChoice"/>.
    /// </summary>
    public sealed class SmartPropChoiceOption
    {
        /// <summary>Option name.</summary>
        public string Name { get; init; } = string.Empty;

        internal List<SmartPropVariableAssignment> Values { get; } = [];

        /// <summary>Names of the variables the option sets.</summary>
        public IEnumerable<string> VariableNames => Values.Select(static v => v.TargetName);
    }

    /// <summary>
    /// A <c>CSmartPropAttributeVariableValue</c>: a typed value written into a named variable.
    /// </summary>
    internal sealed class SmartPropVariableAssignment
    {
        public static readonly string[] DataTypes = ["INVALID", "STRING", "BOOL", "INTEGER", "FLOAT", "VECTOR2", "VECTOR3", "VECTOR4", "COLOR", "ANGLES"];

        public string TargetName { get; }
        public SmartPropVariableType Type { get; }
        public SmartPropAttribute Value { get; }

        public SmartPropVariableAssignment(KVObject? data)
            : this(data?.GetStringProperty("m_TargetName") ?? string.Empty, data?.GetStringProperty("m_DataType") ?? string.Empty, data == null ? null : SmartPropParser.Get(data, "m_Value"))
        {
        }

        public SmartPropVariableAssignment(string targetName, string dataType, KVObject? value)
        {
            TargetName = targetName;
            var typeIndex = Array.FindIndex(DataTypes, t => t.Equals(dataType, StringComparison.OrdinalIgnoreCase));
            Type = typeIndex < 0 ? SmartPropVariableType.Invalid : (SmartPropVariableType)typeIndex;

            var nulls = new SmartPropValue[SmartPropVariableTable.ComponentCount(Type)];
            Value = SmartPropAttribute.Parse(value, SmartPropAttribute.FromLiteral(nulls));
        }

        public SmartPropValue Evaluate(SmartPropContext ctx) => Value.EvaluateToValue(ctx, Type);
    }

    /// <summary>
    /// A loaded smart prop definition (<c>CSmartPropRoot</c>): variables, choices, root modifiers and the element tree.
    /// </summary>
    public sealed class SmartPropDefinition
    {
        /// <summary>Declared variables in authored order.</summary>
        public List<SmartPropVariableDefinition> Variables { get; } = [];

        /// <summary>Choices in authored order.</summary>
        public List<SmartPropChoice> Choices { get; } = [];

        internal SmartPropAttribute MaxDepth { get; }
        internal List<SmartPropModifier> Modifiers { get; } = [];
        internal List<SmartPropElement> Children { get; } = [];

        private readonly Dictionary<string, SmartPropExpression?> expressions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads a definition from a compiled smart prop resource, upgrading the older <c>smartprop1</c> format.
        /// </summary>
        /// <param name="smartProp">The resource.</param>
        /// <returns>The definition.</returns>
        public static SmartPropDefinition Load(SmartProp smartProp)
        {
            var isLegacy = smartProp.Data.Header?.Format.Name is "smartprop0" or "smartprop1";
            return new SmartPropDefinition(smartProp.Data.Root, isLegacy);
        }

        /// <summary>
        /// Loads a definition from a <c>CSmartPropRoot</c> KeyValues3 table.
        /// </summary>
        /// <param name="root">The root table.</param>
        /// <param name="isLegacy">Whether the table is in the <c>smartprop1</c> format.</param>
        /// <returns>The definition.</returns>
        public static SmartPropDefinition Load(KVObject root, bool isLegacy = false) => new(root, isLegacy);

        private SmartPropDefinition(KVObject root, bool isLegacy)
        {
            var parser = new SmartPropParser(isLegacy ? SmartPropLegacyUpgrade.AssignElementIds(root) : null);

            MaxDepth = SmartPropAttribute.Parse(SmartPropParser.Get(root, "m_nMaxDepth"), SmartPropAttribute.FromLiteral(SmartPropValue.FromInt(32)));
            Modifiers.AddRange(SmartPropParser.ParseModifiers(root));
            Children.AddRange(parser.ParseElements(root, "m_Children"));

            foreach (var variable in root.GetArray("m_Variables") ?? [])
            {
                Variables.Add(ParseVariable(variable, parser.ElementId(variable), isLegacy));
            }

            foreach (var choice in root.GetArray("m_Choices") ?? [])
            {
                var parsed = new SmartPropChoice
                {
                    ElementId = (int)choice.GetIntegerProperty("m_nElementID"),
                    Name = choice.GetStringProperty("m_Name") ?? string.Empty,
                    DefaultOption = choice.GetStringProperty("m_DefaultOption") ?? string.Empty,
                };

                foreach (var option in choice.GetArray("m_Options") ?? [])
                {
                    var parsedOption = new SmartPropChoiceOption { Name = option.GetStringProperty("m_Name") ?? string.Empty };

                    foreach (var value in option.GetArray("m_VariableValues") ?? [])
                    {
                        parsedOption.Values.Add(new SmartPropVariableAssignment(value));
                    }

                    parsed.Options.Add(parsedOption);
                }

                Choices.Add(parsed);
            }
        }

        private static SmartPropVariableDefinition ParseVariable(KVObject variable, int elementId, bool isLegacy)
        {
            var className = variable.GetStringProperty("_class") ?? string.Empty;
            SmartPropEnums.VariableClasses.TryGetValue(className, out var enumNames);

            var type = className switch
            {
                "CSmartPropVariable_Bool" => SmartPropVariableType.Bool,
                "CSmartPropVariable_Int" => SmartPropVariableType.Int,
                "CSmartPropVariable_Float" => SmartPropVariableType.Float,
                "CSmartPropVariable_Vector2D" => SmartPropVariableType.Vector2,
                "CSmartPropVariable_Vector3D" => SmartPropVariableType.Vector3,
                "CSmartPropVariable_Vector4D" => SmartPropVariableType.Vector4,
                "CSmartPropVariable_Color" => SmartPropVariableType.Color,
                "CSmartPropVariable_Angles" => SmartPropVariableType.Angles,
                _ => SmartPropVariableType.String,
            };

            var defaultValue = SmartPropParser.Get(variable, "m_DefaultValue") is { } defaultKV
                ? SmartPropVariableTable.Convert(SmartPropValue.FromKV(defaultKV), type)
                : type switch
                {
                    SmartPropVariableType.Bool => SmartPropValue.FromBool(false),
                    SmartPropVariableType.Int => SmartPropValue.FromInt(0),
                    SmartPropVariableType.Float => SmartPropValue.FromFloat(0),
                    SmartPropVariableType.Color => SmartPropValue.FromColor(Color32.White),
                    SmartPropVariableType.String => SmartPropValue.FromString(enumNames?[0] ?? string.Empty),
                    _ => SmartPropVariableTable.Convert(SmartPropValue.Null, type),
                };

            var displayName = variable.GetStringProperty(isLegacy ? "m_ParameterName" : "m_DisplayName")
                ?? variable.GetStringProperty("m_DisplayName")
                ?? string.Empty;

            static double? Number(KVObject data, string key) => data.ContainsKey(key) ? data.GetDoubleProperty(key) : null;

            return new SmartPropVariableDefinition
            {
                Name = variable.GetStringProperty("m_VariableName") ?? string.Empty,
                ClassName = className,
                ElementId = elementId,
                Type = type,
                DefaultValue = defaultValue,
                ExposeAsParameter = variable.GetBooleanProperty("m_bExposeAsParameter"),
                DisplayName = displayName,
                HideExpression = variable.GetStringProperty("m_HideExpression"),
                ReadOnlyExpression = variable.GetStringProperty("m_ReadOnlyExpression"),
                EnumNames = enumNames,
                MinValue = Number(variable, "m_flParamaterMinValue") ?? Number(variable, "m_nParamaterMinValue"),
                MaxValue = Number(variable, "m_flParamaterMaxValue") ?? Number(variable, "m_nParamaterMaxValue"),
                ModelName = variable.GetStringProperty("m_sModelName"),
            };
        }

        internal SmartPropExpression? GetExpression(string text, SmartPropVariableTable variables)
        {
            if (!expressions.TryGetValue(text, out var expression))
            {
                expression = SmartPropExpression.Compile(text, name =>
                {
                    var index = variables.IndexOf(name);
                    return index < 0 ? null : (index, SmartPropVariableTable.ComponentCount(variables.Entries[index].Type));
                });

                expressions[text] = expression;
            }

            return expression;
        }

        /// <summary>
        /// Evaluates an editor expression (hide or read-only) against the variable values this definition starts with.
        /// </summary>
        /// <param name="expression">Expression text.</param>
        /// <param name="input">Overrides that apply to the variables.</param>
        /// <returns>Whether the expression is true; false when it is empty or fails to compile.</returns>
        public bool EvaluateCondition(string? expression, SmartPropEvaluationInput input)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            var variables = SmartPropVariableTable.Build(this, null, input.VariableOverrides, input.ParameterOverrides);
            var values = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in variables.Entries)
            {
                values[entry.Name] = entry.Cache[..SmartPropVariableTable.ComponentCount(entry.Type)];
            }

            return MathF.Abs(SmartPropExpression.Evaluate(expression, values)) > 0.0001f;
        }
    }
}
