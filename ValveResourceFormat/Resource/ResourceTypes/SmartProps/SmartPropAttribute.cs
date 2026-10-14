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

        /// <summary>
        /// Parses one element of <c>m_Components</c>: a binding table, or a literal kept as authored and
        /// converted only when the component is evaluated.
        /// </summary>
        public static SmartPropAttributeComponent Parse(KVObject value)
        {
            if (value.ValueType != KVValueType.Collection)
            {
                return FromLiteral(SmartPropValue.FromKV(value));
            }

            if (value.TryGetValue("m_SourceName", out var source))
            {
                return new(SmartPropComponentKind.Variable, default, SmartPropValue.FromKV(source).GetString());
            }

            if (value.TryGetValue("m_Expression", out var expression))
            {
                return new(SmartPropComponentKind.Expression, default, SmartPropValue.FromKV(expression).GetString());
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

        /// <summary>Value type; a literal is converted to it when the attribute is loaded.</summary>
        public SmartPropVariableType Type { get; }

        /// <summary>Whether this is an enum attribute, whose literal is an enumerator name.</summary>
        public bool IsEnum { get; }

        public SmartPropAttributeComponent[] Components { get; }

        public string? SourceName => Mode == SmartPropAttributeMode.Variable ? Components[0].Text : null;

        private SmartPropAttribute(SmartPropAttributeMode mode, SmartPropVariableType type, bool isEnum, SmartPropAttributeComponent[] components)
        {
            Mode = mode;
            Type = type;
            IsEnum = isEnum;
            Components = components;
        }

        public static SmartPropAttribute FromLiteral(SmartPropVariableType type, params SmartPropValue[] components)
            => new(SmartPropAttributeMode.Literal, type, false, [.. components.Select(SmartPropAttributeComponent.FromLiteral)]);

        /// <summary>
        /// A literal attribute whose type follows from its components: one scalar has its own type, and two to four
        /// components are a vector.
        /// </summary>
        public static SmartPropAttribute FromLiteral(params SmartPropValue[] components)
        {
            var type = components.Length switch
            {
                1 => components[0].Kind switch
                {
                    SmartPropValueKind.Bool => SmartPropVariableType.Bool,
                    SmartPropValueKind.Int => SmartPropVariableType.Int,
                    SmartPropValueKind.Float => SmartPropVariableType.Float,
                    SmartPropValueKind.String => SmartPropVariableType.String,
                    _ => SmartPropVariableType.Invalid,
                },
                2 => SmartPropVariableType.Vector2,
                3 => SmartPropVariableType.Vector3,
                4 => SmartPropVariableType.Vector4,
                _ => SmartPropVariableType.Invalid,
            };

            return FromLiteral(type, components);
        }

        public static SmartPropAttribute FromEnumLiteral(int value)
            => new(SmartPropAttributeMode.Literal, SmartPropVariableType.Int, true, [SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromInt(value))]);

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
                return defaultValue.With(SmartPropAttributeMode.Literal, defaultValue.ConvertLiteral(SmartPropValue.FromKV(value)));
            }

            var components = (SmartPropAttributeComponent[])defaultValue.Components.Clone();

            if (value.TryGetValue("m_Components", out var componentsValue))
            {
                if (componentsValue!.ValueType == KVValueType.Array)
                {
                    var elements = (IReadOnlyList<KVObject>)componentsValue.Values;

                    for (var i = 0; i < components.Length && i < elements.Count; i++)
                    {
                        components[i] = SmartPropAttributeComponent.Parse(elements[i]);
                    }
                }

                return defaultValue.With(SmartPropAttributeMode.Components, components);
            }

            if (value.TryGetValue("m_SourceName", out var source))
            {
                components[0] = new(SmartPropComponentKind.Variable, default, SmartPropValue.FromKV(source).GetString());
                return defaultValue.With(SmartPropAttributeMode.Variable, components);
            }

            if (value.TryGetValue("m_Expression", out var expression))
            {
                components[0] = new(SmartPropComponentKind.Expression, default, SmartPropValue.FromKV(expression).GetString());
                return defaultValue.With(SmartPropAttributeMode.Expression, components);
            }

            return defaultValue;
        }

        private SmartPropAttribute With(SmartPropAttributeMode mode, SmartPropAttributeComponent[] components) => new(mode, Type, IsEnum, components);

        /// <summary>
        /// Converts a whole-value literal to the attribute type. Enum literals stay names until evaluated, and an
        /// untyped attribute keeps the value as is.
        /// </summary>
        private SmartPropAttributeComponent[] ConvertLiteral(SmartPropValue literal)
        {
            var components = (SmartPropAttributeComponent[])Components.Clone();

            switch (Type)
            {
                case SmartPropVariableType.Invalid:
                    components[0] = SmartPropAttributeComponent.FromLiteral(literal);
                    break;
                case SmartPropVariableType.String:
                    components[0] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromString(literal.GetString()));
                    break;
                case SmartPropVariableType.Bool:
                    components[0] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromBool(literal.GetBool()));
                    break;
                case SmartPropVariableType.Int:
                    components[0] = SmartPropAttributeComponent.FromLiteral(IsEnum ? SmartPropValue.FromString(literal.GetString()) : SmartPropValue.FromInt(literal.GetInt()));
                    break;
                case SmartPropVariableType.Float:
                    components[0] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromFloat(literal.GetFloat()));
                    break;
                case SmartPropVariableType.Color:
                {
                    var color = literal.GetColor();
                    components[0] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromInt(color.R));
                    components[1] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromInt(color.G));
                    components[2] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromInt(color.B));
                    components[3] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromInt(color.A));
                    break;
                }
                default:
                {
                    var vector = literal.GetVector(components.Length);

                    for (var i = 0; i < components.Length; i++)
                    {
                        components[i] = SmartPropAttributeComponent.FromLiteral(SmartPropValue.FromFloat(vector[i]));
                    }

                    break;
                }
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
                SmartPropComponentKind.Expression => SmartPropValue.TruncateToInt32(ctx.EvaluateExpression(component.Text!)),
                _ => component.Literal.GetInt(),
            };
        }

        public bool EvaluateBool(SmartPropContext ctx)
        {
            var component = Components[0];

            return component.Kind switch
            {
                SmartPropComponentKind.Variable => ctx.GetVariable(component.Text!).GetBool(),
                SmartPropComponentKind.Expression => ctx.EvaluateExpressionBool(component.Text!),
                _ => component.Literal.GetBool(),
            };
        }

        public int EvaluateInt(SmartPropContext ctx) => ComponentInt(ctx, 0);

        public float EvaluateFloat(SmartPropContext ctx) => ComponentFloat(ctx, 0);

        public string EvaluateString(SmartPropContext ctx)
        {
            var component = Components[0];

            return component.Kind switch
            {
                SmartPropComponentKind.Variable => ctx.GetVariable(component.Text!).GetString(),
                SmartPropComponentKind.Expression => string.Empty,
                _ => component.Literal.GetString(),
            };
        }

        /// <summary>
        /// Evaluates an enum attribute. A whole-value literal or binding is an enumerator name (unknown names are 0);
        /// a component binding or literal reads as an integer.
        /// </summary>
        public int EvaluateEnum(SmartPropContext ctx, string[] names)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return SmartPropEnums.FromName(names, ctx.GetVariable(SourceName!).GetString());
            }

            var component = Components[0];

            if (Mode == SmartPropAttributeMode.Literal && component.Kind == SmartPropComponentKind.Literal && component.Literal.Kind == SmartPropValueKind.String)
            {
                return SmartPropEnums.FromName(names, component.Literal.GetString());
            }

            return ComponentInt(ctx, 0);
        }

        public Vector4 EvaluateVector(SmartPropContext ctx)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return ctx.GetVariable(SourceName!).GetVector(Components.Length);
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
        /// An untyped binding or expression gives null.
        /// </summary>
        public SmartPropValue EvaluateToValue(SmartPropContext ctx, SmartPropVariableType type)
        {
            if (Mode == SmartPropAttributeMode.Variable)
            {
                return type == SmartPropVariableType.Invalid ? SmartPropValue.Null : SmartPropVariableTable.Convert(ctx.GetVariable(SourceName!), type);
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
                    var items = new double[SmartPropVariableTable.ComponentCount(type)];

                    for (var i = 0; i < items.Length; i++)
                    {
                        items[i] = vector[i];
                    }

                    return SmartPropValue.FromArray(items);
                }
                default:
                    return Mode == SmartPropAttributeMode.Literal ? Components[0].Literal : SmartPropValue.Null;
            }
        }
    }
}
