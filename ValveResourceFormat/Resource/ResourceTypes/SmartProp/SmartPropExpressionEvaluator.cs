using System.Collections.Concurrent;
using System.Globalization;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Evaluates SmartProp expression strings such as "InstanceIndex() * 32" or
    /// "(count > 2) ? 16 : 0" to a float. A hand written recursive descent parser
    /// produces an AST that is then walked, so nothing outside the expression
    /// grammar can execute. Never throws; any failure returns the default value.
    /// </summary>
    public static class SmartPropExpressionEvaluator
    {
        /// <summary>
        /// Evaluates an expression to a float. Bare names resolve to variables from
        /// <paramref name="context"/> (unknown names are 0). Never throws.
        /// </summary>
        /// <param name="expression">Expression text, e.g. "InstanceIndex() * 32".</param>
        /// <param name="context">Evaluation context supplying variables and instance state.</param>
        /// <param name="defaultResult">Value returned when the expression cannot be evaluated.</param>
        public static float Evaluate(string? expression, SmartPropEvaluationContext? context = null, float defaultResult = 0f)
        {
            if (string.IsNullOrEmpty(expression))
            {
                return defaultResult;
            }

            try
            {
                var text = StripComments(expression).Trim();
                if (text.Length == 0)
                {
                    return defaultResult;
                }

                var node = ParseCached(text);
                context ??= NullContext;
                return EvaluateNode(node, context);
            }
#pragma warning disable CA1031 // Expression evaluation is a public entry point for untrusted file content and must never throw.
            catch
            {
                return defaultResult;
            }
#pragma warning restore CA1031
        }

        private static readonly SmartPropEvaluationContext NullContext = new();
        private static readonly ConcurrentDictionary<string, Node> AstCache = new();
        private const int AstCacheCapacity = 1024;

        private static readonly Dictionary<string, float> Constants = new(StringComparer.OrdinalIgnoreCase)
        {
            ["true"] = 1f,
            ["false"] = 0f,
            ["pi"] = MathF.PI,
            ["e"] = MathF.E,
        };

        private static readonly Dictionary<string, int> MemberIndices = new(StringComparer.OrdinalIgnoreCase)
        {
            ["x"] = 0,
            ["y"] = 1,
            ["z"] = 2,
            ["w"] = 3,
            ["r"] = 0,
            ["g"] = 1,
            ["b"] = 2,
            ["a"] = 3,
        };

        private static readonly Dictionary<string, Func<SmartPropEvaluationContext, float[], float>> Functions = new()
        {
            ["abs"] = static (_, a) => MathF.Abs(Arg(a, 0)),
            ["min"] = static (_, a) => Fold(a, MathF.Min),
            ["max"] = static (_, a) => Fold(a, MathF.Max),
            ["clamp"] = static (_, a) => MathF.Max(Arg(a, 1), MathF.Min(Arg(a, 0), Arg(a, 2))),
            ["lerp"] = static (_, a) => Arg(a, 0) + (Arg(a, 1) - Arg(a, 0)) * Arg(a, 2),
            ["sign"] = static (_, a) => (Arg(a, 0) > 0f ? 1f : 0f) - (Arg(a, 0) < 0f ? 1f : 0f),
            ["sqrt"] = static (_, a) => Arg(a, 0) >= 0f ? MathF.Sqrt(Arg(a, 0)) : 0f,
            ["pow"] = static (_, a) => MathF.Pow(Arg(a, 0), Arg(a, 1)),
            ["floor"] = static (_, a) => MathF.Floor(Arg(a, 0)),
            ["ceil"] = static (_, a) => MathF.Ceiling(Arg(a, 0)),
            ["round"] = static (_, a) => MathF.Round(Arg(a, 0)),
            ["sin"] = static (_, a) => MathF.Sin(Arg(a, 0)),
            ["cos"] = static (_, a) => MathF.Cos(Arg(a, 0)),
            ["tan"] = static (_, a) => MathF.Tan(Arg(a, 0)),
            ["asin"] = static (_, a) => MathF.Asin(Math.Clamp(Arg(a, 0), -1f, 1f)),
            ["acos"] = static (_, a) => MathF.Acos(Math.Clamp(Arg(a, 0), -1f, 1f)),
            ["atan"] = static (_, a) => MathF.Atan(Arg(a, 0)),
            ["atan2"] = static (_, a) => MathF.Atan2(Arg(a, 0), Arg(a, 1)),
            ["deg2rad"] = static (_, a) => Arg(a, 0) * (MathF.PI / 180f),
            ["rad2deg"] = static (_, a) => Arg(a, 0) * (180f / MathF.PI),
            ["instanceindex"] = static (ctx, _) => ctx.InstanceIndex,
            ["instancecount"] = static (ctx, _) => ctx.InstanceCount,
            ["randomint"] = static (ctx, a) =>
            {
                if (a.Length < 2)
                {
                    return 0f;
                }

                var lo = (int)MathF.Min(a[0], a[1]);
                var hi = (int)MathF.Max(a[0], a[1]);
                return ctx.Rng.Next(lo, hi + 1);
            },
            ["randomfloat"] = static (ctx, a) =>
            {
                if (a.Length < 2)
                {
                    return 0f;
                }

                var lo = MathF.Min(a[0], a[1]);
                var hi = MathF.Max(a[0], a[1]);
                return lo + (float)ctx.Rng.NextDouble() * (hi - lo);
            },
            ["linearscale"] = static (ctx, a) => LinearScale(ctx, a),
        };

        private static float Arg(float[] args, int index) => index < args.Length ? args[index] : 0f;

        private static float Fold(float[] args, Func<float, float, float> combine)
        {
            if (args.Length == 0)
            {
                return 0f;
            }

            var result = args[0];
            for (var i = 1; i < args.Length; i++)
            {
                result = combine(result, args[i]);
            }

            return result;
        }

        private static float LinearScale(SmartPropEvaluationContext context, float[] a)
        {
            // LinearScale(value, inLo, inHi, outLo, outHi) remaps; with three args it
            // normalizes to 0..1; with none it yields the placement's linear scale.
            if (a.Length >= 5)
            {
                if (a[2] == a[1])
                {
                    return a[3];
                }

                var t = (a[0] - a[1]) / (a[2] - a[1]);
                return a[3] + (t * (a[4] - a[3]));
            }

            if (a.Length >= 3)
            {
                return a[2] == a[1] ? 0f : (a[0] - a[1]) / (a[2] - a[1]);
            }

            if (a.Length == 0)
            {
                return context.LinearScale;
            }

            return a[0];
        }

        private static string StripComments(string text)
        {
            // The expression editor permits "//" line comments.
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var index = lines[i].IndexOf("//", StringComparison.Ordinal);
                if (index >= 0)
                {
                    lines[i] = lines[i][..index];
                }
            }

            return string.Join(' ', lines);
        }

        private static Node ParseCached(string text)
        {
            if (AstCache.TryGetValue(text, out var cached))
            {
                return cached;
            }

            var node = new Parser(Tokenize(text)).Parse();
            if (AstCache.Count >= AstCacheCapacity)
            {
                AstCache.Clear();
            }

            return AstCache.GetOrAdd(text, node);
        }

        private static float EvaluateNode(Node node, SmartPropEvaluationContext context)
        {
            switch (node)
            {
                case NumberNode number:
                    return number.Value;
                case VariableNode variable:
                    return VariableValue(context, variable.Name);
                case MemberNode member:
                    return MemberValue(context, member);
                case UnaryNode unary:
                    var operand = EvaluateNode(unary.Operand, context);
                    return unary.Operator switch
                    {
                        "-" => -operand,
                        _ => Truthy(operand) ? 0f : 1f,
                    };
                case BinaryNode binary:
                    var left = EvaluateNode(binary.Left, context);
                    var right = EvaluateNode(binary.Right, context);
                    return binary.Operator switch
                    {
                        "+" => left + right,
                        "-" => left - right,
                        "*" => left * right,
                        "/" => right != 0f ? left / right : 0f,
                        // C-style fmod: the result carries the sign of the dividend.
                        "%" => right != 0f ? left - (right * MathF.Truncate(left / right)) : 0f,
                        _ => 0f,
                    };
                case ComparisonNode comparison:
                    var cmpLeft = EvaluateNode(comparison.Left, context);
                    var cmpRight = EvaluateNode(comparison.Right, context);
                    var result = comparison.Operator switch
                    {
                        "==" => cmpLeft == cmpRight,
                        "!=" => cmpLeft != cmpRight,
                        "<" => cmpLeft < cmpRight,
                        ">" => cmpLeft > cmpRight,
                        "<=" => cmpLeft <= cmpRight,
                        _ => cmpLeft >= cmpRight,
                    };
                    return result ? 1f : 0f;
                case LogicalNode logical:
                    if (logical.Operator == "&&")
                    {
                        return Truthy(EvaluateNode(logical.Left, context)) && Truthy(EvaluateNode(logical.Right, context)) ? 1f : 0f;
                    }

                    return Truthy(EvaluateNode(logical.Left, context)) || Truthy(EvaluateNode(logical.Right, context)) ? 1f : 0f;
                case ConditionalNode conditional:
                    return Truthy(EvaluateNode(conditional.Condition, context))
                        ? EvaluateNode(conditional.WhenTrue, context)
                        : EvaluateNode(conditional.WhenFalse, context);
                case CallNode call:
                    if (!Functions.TryGetValue(call.FunctionName.ToLowerInvariant(), out var function))
                    {
                        throw new FormatException($"Unknown function '{call.FunctionName}'");
                    }

                    var args = new float[call.Arguments.Length];
                    for (var i = 0; i < call.Arguments.Length; i++)
                    {
                        args[i] = EvaluateNode(call.Arguments[i], context);
                    }

                    try
                    {
                        return function(context, args);
                    }
                    catch (Exception)
                    {
                        return 0f;
                    }

                default:
                    return 0f;
            }
        }

        private static float VariableValue(SmartPropEvaluationContext context, string name)
        {
            if (Constants.TryGetValue(name, out var constant))
            {
                return constant;
            }

            return context.GetVariable(name) switch
            {
                null => 0f,
                bool b => b ? 1f : 0f,
                int i => i,
                long l => l,
                float f => f,
                double d => (float)d,
                float[] { Length: > 0 } v => v[0],
                string s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f,
                _ => 0f,
            };
        }

        private static float MemberValue(SmartPropEvaluationContext context, MemberNode member)
        {
            return context.GetVariable(member.VariableName) switch
            {
                float[] v => member.Component < v.Length ? v[member.Component] : 0f,
                bool b => b ? 1f : 0f,
                int i => i,
                long l => l,
                float f => f,
                double d => (float)d,
                _ => 0f,
            };
        }

        private static bool Truthy(float value) => value != 0f;

        private static Token[] Tokenize(string text)
        {
            List<Token> tokens = [];
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                {
                    var start = i;
                    while (i < text.Length && char.IsDigit(text[i]))
                    {
                        i++;
                    }

                    if (i + 1 < text.Length && text[i] == '.' && char.IsDigit(text[i + 1]))
                    {
                        i++;
                        while (i < text.Length && char.IsDigit(text[i]))
                        {
                            i++;
                        }
                    }

                    tokens.Add(new Token(TokenKind.Number, text[start..i], float.Parse(text[start..i], CultureInfo.InvariantCulture)));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    {
                        i++;
                    }

                    tokens.Add(new Token(TokenKind.Identifier, text[start..i], 0f));
                    continue;
                }

                if (i + 1 < text.Length && text.Substring(i, 2) is "||" or "&&" or "==" or "!=" or "<=" or ">=")
                {
                    tokens.Add(new Token(TokenKind.Operator, text.Substring(i, 2), 0f));
                    i += 2;
                    continue;
                }

                if (c is '+' or '-' or '*' or '/' or '%' or '<' or '>' or '!' or '?' or ':' or '(' or ')' or '.' or ',')
                {
                    tokens.Add(new Token(TokenKind.Operator, c.ToString(), 0f));
                    i++;
                    continue;
                }

                throw new FormatException($"Unexpected character '{c}'");
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, 0f));
            return [.. tokens];
        }

        private enum TokenKind
        {
            Number,
            Identifier,
            Operator,
            End,
        }

        private readonly record struct Token(TokenKind Kind, string Text, float Number);

        private abstract record Node;

        private sealed record NumberNode(float Value) : Node;

        private sealed record VariableNode(string Name) : Node;

        private sealed record MemberNode(string VariableName, int Component) : Node;

        private sealed record UnaryNode(string Operator, Node Operand) : Node;

        private sealed record BinaryNode(string Operator, Node Left, Node Right) : Node;

        private sealed record ComparisonNode(string Operator, Node Left, Node Right) : Node;

        private sealed record LogicalNode(string Operator, Node Left, Node Right) : Node;

        private sealed record ConditionalNode(Node Condition, Node WhenTrue, Node WhenFalse) : Node;

        private sealed record CallNode(string FunctionName, Node[] Arguments) : Node;

        private sealed class Parser(Token[] tokens)
        {
            private int position;

            internal Node Parse()
            {
                var node = ParseTernary();
                if (Peek().Kind != TokenKind.End)
                {
                    throw new FormatException("Unexpected trailing tokens");
                }

                return node;
            }

            private Token Peek() => tokens[position];

            private Token Advance() => tokens[position++];

            private bool Accept(string op)
            {
                var token = Peek();
                if (token.Kind != TokenKind.Operator || token.Text != op)
                {
                    return false;
                }

                position++;
                return true;
            }

            private void Expect(string op)
            {
                if (!Accept(op))
                {
                    throw new FormatException($"Expected '{op}'");
                }
            }

            private Node ParseTernary()
            {
                var condition = ParseLogicalOr();
                if (Accept("?"))
                {
                    var whenTrue = ParseTernary();
                    Expect(":");
                    var whenFalse = ParseTernary();
                    return new ConditionalNode(condition, whenTrue, whenFalse);
                }

                return condition;
            }

            private Node ParseLogicalOr()
            {
                var node = ParseLogicalAnd();
                while (Accept("||"))
                {
                    node = new LogicalNode("||", node, ParseLogicalAnd());
                }

                return node;
            }

            private Node ParseLogicalAnd()
            {
                var node = ParseEquality();
                while (Accept("&&"))
                {
                    node = new LogicalNode("&&", node, ParseEquality());
                }

                return node;
            }

            private Node ParseEquality()
            {
                var node = ParseRelational();
                while (true)
                {
                    if (Accept("=="))
                    {
                        node = new ComparisonNode("==", node, ParseRelational());
                    }
                    else if (Accept("!="))
                    {
                        node = new ComparisonNode("!=", node, ParseRelational());
                    }
                    else
                    {
                        return node;
                    }
                }
            }

            private Node ParseRelational()
            {
                var node = ParseAdditive();
                while (true)
                {
                    if (Accept("<="))
                    {
                        node = new ComparisonNode("<=", node, ParseAdditive());
                    }
                    else if (Accept(">="))
                    {
                        node = new ComparisonNode(">=", node, ParseAdditive());
                    }
                    else if (Accept("<"))
                    {
                        node = new ComparisonNode("<", node, ParseAdditive());
                    }
                    else if (Accept(">"))
                    {
                        node = new ComparisonNode(">", node, ParseAdditive());
                    }
                    else
                    {
                        return node;
                    }
                }
            }

            private Node ParseAdditive()
            {
                var node = ParseMultiplicative();
                while (true)
                {
                    if (Accept("+"))
                    {
                        node = new BinaryNode("+", node, ParseMultiplicative());
                    }
                    else if (Accept("-"))
                    {
                        node = new BinaryNode("-", node, ParseMultiplicative());
                    }
                    else
                    {
                        return node;
                    }
                }
            }

            private Node ParseMultiplicative()
            {
                var node = ParseUnary();
                while (true)
                {
                    if (Accept("*"))
                    {
                        node = new BinaryNode("*", node, ParseUnary());
                    }
                    else if (Accept("/"))
                    {
                        node = new BinaryNode("/", node, ParseUnary());
                    }
                    else if (Accept("%"))
                    {
                        node = new BinaryNode("%", node, ParseUnary());
                    }
                    else
                    {
                        return node;
                    }
                }
            }

            private Node ParseUnary()
            {
                if (Accept("-"))
                {
                    return new UnaryNode("-", ParseUnary());
                }

                if (Accept("!"))
                {
                    return new UnaryNode("!", ParseUnary());
                }

                if (Accept("+"))
                {
                    return ParseUnary();
                }

                return ParsePostfix();
            }

            private Node ParsePostfix()
            {
                var node = ParsePrimary();
                while (Peek() is { Kind: TokenKind.Operator, Text: "." })
                {
                    Advance();
                    var member = Advance();
                    if (member.Kind != TokenKind.Identifier)
                    {
                        throw new FormatException("Expected member name after '.'");
                    }

                    if (!MemberIndices.TryGetValue(member.Text, out var index))
                    {
                        throw new FormatException($"Unknown member '.{member.Text}'");
                    }

                    if (node is not VariableNode variable)
                    {
                        throw new FormatException("Member access is only supported on variables");
                    }

                    node = new MemberNode(variable.Name, index);
                }

                return node;
            }

            private Node ParsePrimary()
            {
                var token = Peek();
                switch (token.Kind)
                {
                    case TokenKind.Number:
                        Advance();
                        return new NumberNode(token.Number);
                    case TokenKind.Identifier:
                        Advance();
                        if (Accept("("))
                        {
                            List<Node> arguments = [];
                            if (!Check(")"))
                            {
                                arguments.Add(ParseTernary());
                                while (Accept(","))
                                {
                                    arguments.Add(ParseTernary());
                                }
                            }

                            Expect(")");
                            return new CallNode(token.Text, [.. arguments]);
                        }

                        return new VariableNode(token.Text);
                    case TokenKind.Operator when token.Text == "(":
                        Advance();
                        var node = ParseTernary();
                        Expect(")");
                        return node;
                    default:
                        throw new FormatException($"Unexpected token '{token.Text}'");
                }
            }

            private bool Check(string op)
            {
                var token = Peek();
                return token.Kind == TokenKind.Operator && token.Text == op;
            }
        }
    }
}
