using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    internal enum SmartPropAttributeMode
    {
        Literal,
        Variable,
        Expression,
        Components,
    }

    internal enum SmartPropComponentKind
    {
        Literal,
        Variable,
        Expression,
    }

    internal readonly record struct SmartPropAttributeComponent(SmartPropComponentKind Kind, SmartPropValue Literal, string? Text)
    {
        public static SmartPropAttributeComponent FromLiteral(SmartPropValue value) => new(SmartPropComponentKind.Literal, value, null);

        public static SmartPropAttributeComponent Parse(KVObject value)
        {
            if (value.ValueType != KVValueType.Collection)
            {
                return FromLiteral(SmartPropValue.FromKV(value));
            }

            if (value.TryGetValue("m_SourceName", out var source))
            {
                return new(SmartPropComponentKind.Variable, default, (string)source!);
            }

            if (value.TryGetValue("m_Expression", out var expression))
            {
                return new(SmartPropComponentKind.Expression, default, (string)expression!);
            }

            return FromLiteral(SmartPropValue.Null);
        }
    }

    /// <summary>
    /// An evaluated attribute: a literal, a whole-value variable binding, an expression on the first
    /// component, or per-component literals, bindings and expressions. Components evaluate last to first.
    /// </summary>
    internal sealed class SmartPropAttribute
    {
        public SmartPropAttributeMode Mode { get; }
        public string? SourceName { get; }
        public SmartPropAttributeComponent[] Components { get; }

        private SmartPropAttribute(SmartPropAttributeMode mode, string? sourceName, SmartPropAttributeComponent[] components)
        {
            Mode = mode;
            SourceName = sourceName;
            Components = components;
        }

        public static SmartPropAttribute FromLiteral(params SmartPropValue[] components)
            => new(SmartPropAttributeMode.Literal, null, [.. components.Select(SmartPropAttributeComponent.FromLiteral)]);

        /// <summary>
        /// Parses an attribute member over its default. A missing member or a table without any of the
        /// three binding keys keeps the default.
        /// </summary>
        public static SmartPropAttribute Parse(KVObject? value, SmartPropAttribute defaultValue)
        {
            if (value == null)
            {
                return defaultValue;
            }

            if (value.ValueType != KVValueType.Collection)
            {
                return new(SmartPropAttributeMode.Literal, null, SplitLiteral(value, defaultValue.Components));
            }

            if (value.TryGetValue("m_Components", out var componentsValue) && componentsValue!.ValueType == KVValueType.Array)
            {
                var components = (SmartPropAttributeComponent[])defaultValue.Components.Clone();
                var elements = (IReadOnlyList<KVObject>)componentsValue.Values;

                for (var i = 0; i < components.Length && i < elements.Count; i++)
                {
                    components[i] = SmartPropAttributeComponent.Parse(elements[i]);
                }

                return new(SmartPropAttributeMode.Components, null, components);
            }

            if (value.TryGetValue("m_SourceName", out var source))
            {
                var components = (SmartPropAttributeComponent[])defaultValue.Components.Clone();
                components[0] = new(SmartPropComponentKind.Variable, default, (string)source!);
                return new(SmartPropAttributeMode.Variable, (string)source!, components);
            }

            if (value.TryGetValue("m_Expression", out var expression))
            {
                var components = (SmartPropAttributeComponent[])defaultValue.Components.Clone();
                components[0] = new(SmartPropComponentKind.Expression, default, (string)expression!);
                return new(SmartPropAttributeMode.Expression, null, components);
            }

            return defaultValue;
        }

        private static SmartPropAttributeComponent[] SplitLiteral(KVObject value, SmartPropAttributeComponent[] defaults)
        {
            var literal = SmartPropValue.FromKV(value);
            var components = (SmartPropAttributeComponent[])defaults.Clone();

            if (components.Length == 1 || literal.Kind != SmartPropValueKind.Array)
            {
                components[0] = SmartPropAttributeComponent.FromLiteral(literal);
                return components;
            }

            for (var i = 0; i < components.Length && i < literal.Count; i++)
            {
                components[i] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromFloat(literal.GetItem(i)));
            }

            return components;
        }

        private float ComponentFloat(SmartPropContext ctx, int index)
        {
            var component = Components[index];

            return component.Kind switch
            {
                SmartPropComponentKind.Variable => ctx.GetVariable(component.Text!).GetFloat(),
                SmartPropComponentKind.Expression => ctx.EvaluateExpression(component.Text!),
                _ => component.Literal.GetFloat(),
            };
        }

        private int ComponentInt(SmartPropContext ctx, int index)
        {
            var component = Components[index];

            return component.Kind switch
            {
                SmartPropComponentKind.Variable => ctx.GetVariable(component.Text!).GetInt(),
                SmartPropComponentKind.Expression => (int)ctx.EvaluateExpression(component.Text!),
                _ => component.Literal.GetInt(),
            };
        }

        public bool EvaluateBool(SmartPropContext ctx)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return ctx.GetVariable(SourceName!).GetBool();
            }

            var component = Components[0];

            return component.Kind switch
            {
                SmartPropComponentKind.Variable => ctx.GetVariable(component.Text!).GetBool(),
                SmartPropComponentKind.Expression => MathF.Abs(ctx.EvaluateExpression(component.Text!)) > 0.0001f,
                _ => component.Literal.GetBool(),
            };
        }

        public int EvaluateInt(SmartPropContext ctx)
            => Mode == SmartPropAttributeMode.Variable ? ctx.GetVariable(SourceName!).GetInt() : ComponentInt(ctx, 0);

        public float EvaluateFloat(SmartPropContext ctx)
            => Mode == SmartPropAttributeMode.Variable ? ctx.GetVariable(SourceName!).GetFloat() : ComponentFloat(ctx, 0);

        public string EvaluateString(SmartPropContext ctx)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return ctx.GetVariable(SourceName!).GetString();
            }

            var component = Components[0];

            return component.Kind switch
            {
                SmartPropComponentKind.Variable => ctx.GetVariable(component.Text!).GetString(),
                SmartPropComponentKind.Expression => string.Empty,
                _ => component.Literal.GetString(),
            };
        }

        public int EvaluateEnum(SmartPropContext ctx, string[] names)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return SmartPropEnums.FromName(names, ctx.GetVariable(SourceName!).GetString());
            }

            var component = Components[0];

            return component.Kind switch
            {
                SmartPropComponentKind.Variable => SmartPropEnums.FromName(names, ctx.GetVariable(component.Text!).GetString()),
                SmartPropComponentKind.Expression => (int)ctx.EvaluateExpression(component.Text!),
                _ => component.Literal.Kind == SmartPropValueKind.String
                    ? SmartPropEnums.FromName(names, component.Literal.GetString())
                    : component.Literal.GetInt(),
            };
        }

        public Vector4 EvaluateVector(SmartPropContext ctx)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return ctx.GetVariable(SourceName!).GetVector();
            }

            var result = Vector4.Zero;

            for (var i = Components.Length - 1; i >= 0; i--)
            {
                result[i] = ComponentFloat(ctx, i);
            }

            return result;
        }

        public Vector3 EvaluateVector3(SmartPropContext ctx)
        {
            var value = EvaluateVector(ctx);
            return new Vector3(value.X, value.Y, value.Z);
        }

        public Vector2 EvaluateVector2(SmartPropContext ctx)
        {
            var value = EvaluateVector(ctx);
            return new Vector2(value.X, value.Y);
        }

        public Color32 EvaluateColor(SmartPropContext ctx)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return ctx.GetVariable(SourceName!).GetColor();
            }

            Span<byte> channels = stackalloc byte[4];

            for (var i = Components.Length - 1; i >= 0; i--)
            {
                channels[i] = unchecked((byte)ComponentInt(ctx, i));
            }

            return new Color32(channels[0], channels[1], channels[2], channels[3]);
        }

        /// <summary>
        /// Evaluates to a value of the given type, as written into a variable by SetVariable and choice options.
        /// </summary>
        public SmartPropValue EvaluateToValue(SmartPropContext ctx, SmartPropVariableType type)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return SmartPropVariableTable.Convert(ctx.GetVariable(SourceName!), type);
            }

            switch (type)
            {
                case SmartPropVariableType.Bool:
                    return SmartPropValue.FromBool(EvaluateBool(ctx));
                case SmartPropVariableType.Int:
                    return SmartPropValue.FromInt(EvaluateInt(ctx));
                case SmartPropVariableType.Float:
                    return SmartPropValue.FromFloat(EvaluateFloat(ctx));
                case SmartPropVariableType.String:
                    return SmartPropValue.FromString(EvaluateString(ctx));
                case SmartPropVariableType.Color:
                    return SmartPropValue.FromColor(EvaluateColor(ctx));
                case SmartPropVariableType.Vector2:
                case SmartPropVariableType.Vector3:
                case SmartPropVariableType.Vector4:
                case SmartPropVariableType.Angles:
                {
                    var vector = EvaluateVector(ctx);
                    var count = SmartPropVariableTable.ComponentCount(type);
                    var items = new double[count];

                    for (var i = 0; i < count; i++)
                    {
                        items[i] = vector[i];
                    }

                    return SmartPropValue.FromArray(items);
                }
                default:
                    return Components[0].Kind == SmartPropComponentKind.Literal ? Components[0].Literal : SmartPropValue.Null;
            }
        }
    }
}
