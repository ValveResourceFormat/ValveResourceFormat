namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// The variables of one smart prop evaluation level: unique case-insensitive names, each with a
    /// value and the float components expressions read.
    /// </summary>
    internal sealed class SmartPropVariableTable
    {
        public sealed class Entry(string name, SmartPropVariableType type)
        {
            public string Name { get; } = name;
            public SmartPropVariableType Type { get; } = type;
            public SmartPropValue Value { get; private set; }
            public float[] Cache { get; } = new float[4];

            public void SetValue(SmartPropValue value)
            {
                Value = value;
                Array.Clear(Cache);

                switch (Type)
                {
                    case SmartPropVariableType.String:
                        Cache[0] = (float)SmartPropValue.StringToFloat(value.GetString());
                        break;
                    case SmartPropVariableType.Bool:
                    case SmartPropVariableType.Int:
                    case SmartPropVariableType.Float:
                        Cache[0] = value.GetFloat();
                        break;
                    case SmartPropVariableType.Color:
                    {
                        var color = value.GetColor();
                        Cache[0] = SmartPropMath.SrgbToLinear(color.R);
                        Cache[1] = SmartPropMath.SrgbToLinear(color.G);
                        Cache[2] = SmartPropMath.SrgbToLinear(color.B);
                        break;
                    }
                    case SmartPropVariableType.Vector2:
                    case SmartPropVariableType.Vector3:
                    case SmartPropVariableType.Vector4:
                    case SmartPropVariableType.Angles:
                        for (var i = 0; i < ComponentCount(Type); i++)
                        {
                            Cache[i] = (float)value.GetItem(i);
                        }

                        break;
                    default:
                        break;
                }
            }
        }

        public List<Entry> Entries { get; } = [];
        private readonly Dictionary<string, int> indices = new(StringComparer.OrdinalIgnoreCase);

        public static int ComponentCount(SmartPropVariableType type) => type switch
        {
            SmartPropVariableType.Vector2 => 2,
            SmartPropVariableType.Vector3 or SmartPropVariableType.Angles => 3,
            SmartPropVariableType.Vector4 or SmartPropVariableType.Color => 4,
            _ => 1,
        };

        public int IndexOf(string name) => indices.TryGetValue(name, out var index) ? index : -1;

        public Entry? Find(string name) => indices.TryGetValue(name, out var index) ? Entries[index] : null;

        /// <summary>
        /// Builds the table of a smart prop: each variable takes the value of a same-named variable of the
        /// enclosing level unchanged, otherwise its default, a name override of the same type and, for an
        /// exposed variable when element id overrides are given, the element id override or again the default.
        /// </summary>
        public static SmartPropVariableTable Build(
            SmartPropDefinition definition,
            SmartPropVariableTable? outer,
            IReadOnlyDictionary<string, SmartPropValue>? nameOverrides,
            IReadOnlyDictionary<int, SmartPropValue>? elementIdOverrides)
        {
            var table = new SmartPropVariableTable();

            foreach (var variable in definition.Variables)
            {
                if (table.indices.ContainsKey(variable.Name))
                {
                    continue;
                }

                var entry = new Entry(variable.Name, variable.Type);
                var inherited = outer?.Find(variable.Name);

                if (inherited != null)
                {
                    entry.SetValue(inherited.Value);
                }
                else
                {
                    var value = variable.DefaultValue;

                    if (nameOverrides != null && nameOverrides.TryGetValue(variable.Name, out var nameOverride) && HasType(nameOverride, variable.Type))
                    {
                        value = nameOverride;
                    }

                    if (elementIdOverrides != null && variable.ExposeAsParameter)
                    {
                        value = elementIdOverrides.TryGetValue(variable.ElementId, out var idOverride) ? idOverride : variable.DefaultValue;
                    }

                    entry.SetValue(value);
                }

                table.indices[variable.Name] = table.Entries.Count;
                table.Entries.Add(entry);
            }

            return table;
        }

        /// <summary>Whether a value has the basic type a variable of the given type stores.</summary>
        public static bool HasType(SmartPropValue value, SmartPropVariableType type) => type switch
        {
            SmartPropVariableType.String => value.Kind == SmartPropValueKind.String,
            SmartPropVariableType.Bool => value.Kind == SmartPropValueKind.Bool,
            SmartPropVariableType.Int => value.Kind == SmartPropValueKind.Int,
            SmartPropVariableType.Float => value.Kind == SmartPropValueKind.Float,
            SmartPropVariableType.Invalid => true,
            _ => value.Kind == SmartPropValueKind.Array,
        };

        /// <summary>Converts a value to the basic type a variable of the given type stores.</summary>
        public static SmartPropValue Convert(SmartPropValue value, SmartPropVariableType type)
        {
            switch (type)
            {
                case SmartPropVariableType.String:
                    return SmartPropValue.FromString(value.GetString());
                case SmartPropVariableType.Bool:
                    return SmartPropValue.FromBool(value.GetBool());
                case SmartPropVariableType.Int:
                    return SmartPropValue.FromInt(value.GetInt());
                case SmartPropVariableType.Float:
                    return value.Kind == SmartPropValueKind.Float ? value : SmartPropValue.FromFloat(value.GetFloat());
                case SmartPropVariableType.Color:
                    return SmartPropValue.FromColor(value.GetColor());
                case SmartPropVariableType.Vector2:
                case SmartPropVariableType.Vector3:
                case SmartPropVariableType.Vector4:
                case SmartPropVariableType.Angles:
                {
                    var items = new double[ComponentCount(type)];

                    for (var i = 0; i < items.Length; i++)
                    {
                        items[i] = value.GetItem(i);
                    }

                    return SmartPropValue.FromArray(items);
                }
                default:
                    return value;
            }
        }
    }
}
