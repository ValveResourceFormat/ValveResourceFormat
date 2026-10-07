using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Runtime values the smart prop expression functions read.
    /// </summary>
    internal interface ISmartPropExpressionContext
    {
        float GetVariableComponent(int variableIndex, int component);
        int InstanceCount { get; }
        int InstanceIndex { get; }
        int EvaluationDepth { get; }
        int MaxEvaluationDepth { get; }
        float PathParameter { get; }
        float LinearScale { get; }
        float LineLength { get; }
        float UScale { get; }
        float VScale { get; }
        UniformRandomStream GetRandomStream();
    }

    /// <summary>
    /// A compiled smart prop expression. Everything evaluates as a 32-bit float, truth is a nonzero value,
    /// and any lexer, parser or name resolution error fails the whole expression, which then evaluates to 0.
    /// </summary>
    public sealed class SmartPropExpression
    {
        private readonly Node root;

        private SmartPropExpression(Node root)
        {
            this.root = root;
        }

        /// <summary>
        /// Compiles an expression whose variables are given as float components.
        /// </summary>
        /// <param name="text">Expression text.</param>
        /// <param name="variables">Variables by name (case-insensitive), each with one to four float components.</param>
        /// <returns>The compiled expression, or null when it fails to compile.</returns>
        public static SmartPropExpression? Compile(string text, IReadOnlyDictionary<string, float[]>? variables = null)
        {
            if (variables == null)
            {
                return Compile(text, static _ => null);
            }

            var names = variables.Keys.ToList();
            var expression = Compile(text, name =>
            {
                var index = names.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? null : (index, variables[names[index]].Length);
            });

            return expression;
        }

        /// <summary>
        /// Evaluates an expression outside a smart prop evaluation: instance functions return their defaults and
        /// <c>RandomFloat</c>/<c>RandomInt</c> return 0.
        /// </summary>
        /// <param name="text">Expression text.</param>
        /// <param name="variables">Variables by name (case-insensitive), each with one to four float components.</param>
        /// <returns>The result, 0 when the expression fails to compile.</returns>
        public static float Evaluate(string text, IReadOnlyDictionary<string, float[]>? variables = null)
        {
            if (variables == null)
            {
                return Compile(text, static _ => null)?.Evaluate(null) ?? 0f;
            }

            var names = variables.Keys.ToList();
            var expression = Compile(text, name =>
            {
                var index = names.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? null : (index, variables[names[index]].Length);
            });

            return expression?.Evaluate(new DictionaryContext([.. names.Select(n => variables[n])])) ?? 0f;
        }

        internal static SmartPropExpression? Compile(string text, Func<string, (int Index, int Components)?> resolveVariable)
        {
            try
            {
                var tokens = Lexer.Tokenize(text);
                var parser = new Parser(tokens, resolveVariable);
                return new SmartPropExpression(parser.ParseRoot());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        internal float Evaluate(ISmartPropExpressionContext? context) => root.Evaluate(context);

        private sealed class DictionaryContext(float[][] values) : ISmartPropExpressionContext
        {
            public float GetVariableComponent(int variableIndex, int component) => values[variableIndex][component];
            public int InstanceCount => 0;
            public int InstanceIndex => 0;
            public int EvaluationDepth => 0;
            public int MaxEvaluationDepth => 0;
            public float PathParameter => 0f;
            public float LinearScale => 1f;
            public float LineLength => 0f;
            public float UScale => 0f;
            public float VScale => 0f;
            public UniformRandomStream GetRandomStream() => throw new InvalidOperationException();
        }

        private enum TokenType
        {
            End,
            LeftParen,
            RightParen,
            Comma,
            Colon,
            Question,
            Dot,
            Not,
            Star,
            Slash,
            Percent,
            Plus,
            Minus,
            Less,
            Greater,
            LessEqual,
            GreaterEqual,
            Equal,
            NotEqual,
            And,
            Or,
            True,
            False,
            Int,
            Float,
            Identifier,
            String,
            Unused,
        }

        private readonly record struct Token(TokenType Type, string Text);

        private static class Lexer
        {
            private static bool IsIdChar(char c) => char.IsAsciiLetter(c) || c == '_';

            public static List<Token> Tokenize(string text)
            {
                var tokens = new List<Token>();
                var i = 0;

                while (i < text.Length)
                {
                    var c = text[i];

                    if (c is ' ' or '\t' or '\n' or '\r')
                    {
                        i++;
                        continue;
                    }

                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                    {
                        var newline = text.IndexOf('\n', i);

                        if (newline < 0)
                        {
                            throw new FormatException("Line comment without a newline");
                        }

                        i = newline + 1;
                        continue;
                    }

                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                    {
                        var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);

                        if (close < 0)
                        {
                            throw new FormatException("Unterminated block comment");
                        }

                        i = close + 2;
                        continue;
                    }

                    if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
                    {
                        tokens.Add(LexNumber(text, ref i));
                        continue;
                    }

                    if (IsIdChar(c) || c is '$' or '@')
                    {
                        var start = i;

                        if (c is '$' or '@')
                        {
                            i++;

                            if (i >= text.Length || !IsIdChar(text[i]))
                            {
                                throw new FormatException("Scope qualifier without a name");
                            }
                        }

                        while (i < text.Length && (IsIdChar(text[i]) || char.IsAsciiDigit(text[i])))
                        {
                            i++;
                        }

                        if (i < text.Length && text[i] == ':')
                        {
                            if (i + 1 >= text.Length || text[i + 1] != ':')
                            {
                                throw new FormatException("Identifier followed by a single ':'");
                            }

                            i += 2;

                            while (i < text.Length && (IsIdChar(text[i]) || char.IsAsciiDigit(text[i])))
                            {
                                i++;
                            }
                        }

                        var word = text[start..i];
                        tokens.Add(word switch
                        {
                            "true" => new Token(TokenType.True, word),
                            "false" => new Token(TokenType.False, word),
                            _ => new Token(TokenType.Identifier, word),
                        });
                        continue;
                    }

                    if (c is '"' or '\'')
                    {
                        throw new FormatException("String literals are not supported");
                    }

                    var next = i + 1 < text.Length ? text[i + 1] : '\0';
                    var (type, length) = (c, next) switch
                    {
                        ('<', '=') => (TokenType.LessEqual, 2),
                        ('>', '=') => (TokenType.GreaterEqual, 2),
                        ('=', '=') => (TokenType.Equal, 2),
                        ('!', '=') => (TokenType.NotEqual, 2),
                        ('&', '&') => (TokenType.And, 2),
                        ('|', '|') => (TokenType.Or, 2),
                        ('(', _) => (TokenType.LeftParen, 1),
                        (')', _) => (TokenType.RightParen, 1),
                        (',', _) => (TokenType.Comma, 1),
                        (':', _) => (TokenType.Colon, 1),
                        ('?', _) => (TokenType.Question, 1),
                        ('.', _) => (TokenType.Dot, 1),
                        ('!', _) => (TokenType.Not, 1),
                        ('*', _) => (TokenType.Star, 1),
                        ('/', _) => (TokenType.Slash, 1),
                        ('%', _) => (TokenType.Percent, 1),
                        ('+', _) => (TokenType.Plus, 1),
                        ('-', _) => (TokenType.Minus, 1),
                        ('<', _) => (TokenType.Less, 1),
                        ('>', _) => (TokenType.Greater, 1),
                        ('{' or '}' or '[' or ']' or ';' or '=', _) => (TokenType.Unused, 1),
                        _ => throw new FormatException($"Unexpected character '{c}'"),
                    };

                    tokens.Add(new Token(type, text.Substring(i, length)));
                    i += length;
                }

                tokens.Add(new Token(TokenType.End, string.Empty));
                return tokens;
            }

            private static Token LexNumber(string text, ref int i)
            {
                var start = i;

                if (text[i] == '0' && i + 1 < text.Length && (text[i + 1] is 'x' or 'X'))
                {
                    i += 2;
                    var digitsStart = i;

                    while (i < text.Length && (char.IsAsciiHexDigit(text[i]) || text[i] == '\''))
                    {
                        i++;
                    }

                    if (i == digitsStart)
                    {
                        throw new FormatException("Hex literal without digits");
                    }

                    return new Token(TokenType.Int, text[start..i]);
                }

                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                var isFloat = false;

                if (i < text.Length && text[i] == '.')
                {
                    isFloat = true;
                    i++;

                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                    {
                        i++;
                    }
                }

                if (i < text.Length && text[i] is 'e' or 'E')
                {
                    var exponent = i + 1;

                    if (exponent < text.Length && text[exponent] is '+' or '-')
                    {
                        exponent++;
                    }

                    if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
                    {
                        isFloat = true;
                        i = exponent;

                        while (i < text.Length && char.IsAsciiDigit(text[i]))
                        {
                            i++;
                        }
                    }
                }

                if (!isFloat)
                {
                    while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '\''))
                    {
                        i++;
                    }

                    return new Token(TokenType.Int, text[start..i]);
                }

                return new Token(TokenType.Float, text[start..i]);
            }
        }

        private sealed class Parser(List<Token> tokens, Func<string, (int Index, int Components)?> resolveVariable)
        {
            private int position;

            private Token Peek => tokens[position];

            private Token Next() => tokens[position++];

            private bool Accept(TokenType type)
            {
                if (Peek.Type != type)
                {
                    return false;
                }

                position++;
                return true;
            }

            private void Expect(TokenType type)
            {
                if (!Accept(type))
                {
                    throw new FormatException($"Expected {type}");
                }
            }

            public Node ParseRoot()
            {
                var node = ParseConditional();
                Expect(TokenType.End);
                return node;
            }

            private Node ParseConditional()
            {
                var condition = ParseLogicalOr();

                if (!Accept(TokenType.Question))
                {
                    return condition;
                }

                var whenTrue = ParseLogicalOr();
                Expect(TokenType.Colon);
                var whenFalse = ParseLogicalOr();
                return Fold(new TernaryNode(condition, whenTrue, whenFalse));
            }

            private Node ParseLogicalOr()
            {
                var left = ParseLogicalAnd();

                while (Accept(TokenType.Or))
                {
                    left = Fold(new BinaryNode(TokenType.Or, left, ParseLogicalAnd()));
                }

                return left;
            }

            private Node ParseLogicalAnd()
            {
                var left = ParseEquality();

                while (Accept(TokenType.And))
                {
                    left = Fold(new BinaryNode(TokenType.And, left, ParseEquality()));
                }

                return left;
            }

            private Node ParseEquality()
            {
                var left = ParseRelational();

                if (Peek.Type is TokenType.Equal or TokenType.NotEqual)
                {
                    var op = Next().Type;
                    left = Fold(new BinaryNode(op, left, ParseRelational()));
                }

                return left;
            }

            private Node ParseRelational()
            {
                var left = ParseAdditive();

                if (Peek.Type is TokenType.Less or TokenType.Greater or TokenType.LessEqual or TokenType.GreaterEqual)
                {
                    var op = Next().Type;
                    left = Fold(new BinaryNode(op, left, ParseAdditive()));
                }

                return left;
            }

            private Node ParseAdditive()
            {
                var left = ParseMultiplicative();

                while (Peek.Type is TokenType.Plus or TokenType.Minus)
                {
                    var op = Next().Type;
                    left = Fold(new BinaryNode(op, left, ParseMultiplicative()));
                }

                return left;
            }

            private Node ParseMultiplicative()
            {
                var left = ParseUnary();

                while (Peek.Type is TokenType.Star or TokenType.Slash or TokenType.Percent)
                {
                    var op = Next().Type;
                    left = Fold(new BinaryNode(op, left, ParseUnary()));
                }

                return left;
            }

            private Node ParseUnary()
            {
                if (Peek.Type is TokenType.Minus or TokenType.Not)
                {
                    var op = Next().Type;
                    return Fold(new UnaryNode(op, ParseProperty()));
                }

                return ParseProperty();
            }

            private Node ParseProperty()
            {
                var start = Peek;
                var node = ParsePrimary();

                if (!Accept(TokenType.Dot))
                {
                    return node;
                }

                var member = Next();

                if (member.Type != TokenType.Identifier || node is not VariableNode variable || start.Type != TokenType.Identifier)
                {
                    throw new FormatException("Properties are only allowed on variables");
                }

                var component = member.Text.Length != 1 ? -1 : char.ToLowerInvariant(member.Text[0]) switch
                {
                    'x' or 'r' => 0,
                    'y' or 'g' => 1,
                    'z' or 'b' => 2,
                    'w' or 'a' => 3,
                    _ => -1,
                };

                if (component < 0 || component >= variable.Components)
                {
                    throw new FormatException($"Invalid component '{member.Text}'");
                }

                return new VariableNode(variable.Index, component, variable.Components);
            }

            private Node ParsePrimary()
            {
                var token = Next();

                switch (token.Type)
                {
                    case TokenType.LeftParen:
                    {
                        var inner = ParseConditional();
                        Expect(TokenType.RightParen);
                        return inner;
                    }
                    case TokenType.True:
                        return new ConstantNode(1f);
                    case TokenType.False:
                        return new ConstantNode(0f);
                    case TokenType.Int:
                        return new ConstantNode(ParseInt(token.Text));
                    case TokenType.Float:
                        return new ConstantNode((float)double.Parse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture));
                    case TokenType.Identifier:
                        return Peek.Type == TokenType.LeftParen ? ParseCall(token.Text) : ResolveIdentifier(token.Text, Peek.Type == TokenType.Dot);
                    default:
                        throw new FormatException($"Unexpected token '{token.Text}'");
                }
            }

            private static float ParseInt(string text)
            {
                if (text.Length > 2 && text[1] is 'x' or 'X')
                {
                    long hex = 0;

                    foreach (var c in text.AsSpan(2))
                    {
                        if (!char.IsAsciiHexDigit(c))
                        {
                            break;
                        }

                        hex = hex * 16 + Convert.ToInt32(c.ToString(), 16);
                    }

                    return hex;
                }

                long value = 0;

                foreach (var c in text)
                {
                    if (!char.IsAsciiDigit(c))
                    {
                        break;
                    }

                    value = value * 10 + (c - '0');
                }

                return value;
            }

            private Node ResolveIdentifier(string name, bool hasProperty)
            {
                if (name.StartsWith('$'))
                {
                    throw new FormatException("Disallowed variable name");
                }

                if (!hasProperty)
                {
                    if (name.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ConstantNode(1f);
                    }

                    if (name.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ConstantNode(0f);
                    }

                    if (name.Equals("pi", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ConstantNode(3.1415927f);
                    }
                }

                if (resolveVariable(name) is not var (index, components))
                {
                    throw new FormatException($"Unknown variable '{name}'");
                }

                if (!hasProperty && components != 1)
                {
                    throw new FormatException("A specific component must be specified");
                }

                return new VariableNode(index, 0, components);
            }

            private Node ParseCall(string name)
            {
                Expect(TokenType.LeftParen);
                var args = new List<Node>();

                if (!Accept(TokenType.RightParen))
                {
                    do
                    {
                        args.Add(ParseConditional());
                    }
                    while (Accept(TokenType.Comma));

                    Expect(TokenType.RightParen);
                }

                var lower = name.ToLowerInvariant();

                if (Builtins.TryGetValue(lower, out var builtin))
                {
                    if (builtin.Arity != args.Count)
                    {
                        throw new FormatException($"Wrong argument count for '{name}'");
                    }

                    return Fold(new BuiltinNode(builtin.Function, [.. args]));
                }

                if (ContextFunctions.TryGetValue(lower, out var function))
                {
                    if (function.Arity != args.Count)
                    {
                        throw new FormatException($"Wrong argument count for '{name}'");
                    }

                    return new ContextFunctionNode(function.Function, [.. args]);
                }

                throw new FormatException($"Unknown function '{name}'");
            }

            private static Node Fold(Node node) => node.IsConstant ? new ConstantNode(node.EvaluateFolded()) : node;
        }

        private abstract class Node
        {
            public abstract bool IsConstant { get; }
            public abstract float Evaluate(ISmartPropExpressionContext? context);
            public virtual float EvaluateFolded() => Evaluate(null);
        }

        private sealed class ConstantNode(float value) : Node
        {
            public override bool IsConstant => true;
            public override float Evaluate(ISmartPropExpressionContext? context) => value;
        }

        private sealed class VariableNode(int index, int component, int components) : Node
        {
            public int Index => index;
            public int Components => components;
            public override bool IsConstant => false;
            public override float Evaluate(ISmartPropExpressionContext? context) => context?.GetVariableComponent(index, component) ?? 0f;
        }

        private sealed class UnaryNode(TokenType op, Node operand) : Node
        {
            public override bool IsConstant => operand.IsConstant;

            public override float Evaluate(ISmartPropExpressionContext? context)
            {
                var value = operand.Evaluate(context);
                return op == TokenType.Minus ? -value : (value == 0f ? 1f : 0f);
            }
        }

        private sealed class BinaryNode(TokenType op, Node left, Node right) : Node
        {
            public override bool IsConstant => left.IsConstant && right.IsConstant;

            public override float Evaluate(ISmartPropExpressionContext? context)
            {
                var a = left.Evaluate(context);

                switch (op)
                {
                    case TokenType.And:
                        return a == 0f ? a : right.Evaluate(context);
                    case TokenType.Or:
                        return a != 0f ? a : right.Evaluate(context);
                    default:
                        break;
                }

                var b = right.Evaluate(context);

                return op switch
                {
                    TokenType.Star => a * b,
                    TokenType.Slash => a / b,
                    TokenType.Percent => a % b,
                    TokenType.Plus => a + b,
                    TokenType.Minus => a - b,
                    TokenType.Less => a < b ? 1f : 0f,
                    TokenType.Greater => a > b ? 1f : 0f,
                    TokenType.LessEqual => a <= b ? 1f : 0f,
                    TokenType.GreaterEqual => a >= b ? 1f : 0f,
                    TokenType.Equal => MathF.Abs(a - b) <= 0.001f ? 1f : 0f,
                    TokenType.NotEqual => MathF.Abs(a - b) > 0.001f ? 1f : 0f,
                    _ => throw new UnreachableException(),
                };
            }

            public override float EvaluateFolded()
            {
                var a = left.EvaluateFolded();
                var b = right.EvaluateFolded();

                return op switch
                {
                    TokenType.Equal => a == b ? 1f : 0f,
                    TokenType.NotEqual => a != b ? 1f : 0f,
                    TokenType.And => a != 0f && b != 0f ? 1f : 0f,
                    TokenType.Or => a != 0f || b != 0f ? 1f : 0f,
                    _ => Evaluate(null),
                };
            }
        }

        private sealed class TernaryNode(Node condition, Node whenTrue, Node whenFalse) : Node
        {
            public override bool IsConstant => condition.IsConstant && whenTrue.IsConstant && whenFalse.IsConstant;

            public override float Evaluate(ISmartPropExpressionContext? context)
                => condition.Evaluate(context) != 0f ? whenTrue.Evaluate(context) : whenFalse.Evaluate(context);
        }

        private sealed class BuiltinNode(Func<float[], float> function, Node[] args) : Node
        {
            public override bool IsConstant => args.All(static arg => arg.IsConstant);

            public override float Evaluate(ISmartPropExpressionContext? context)
            {
                var values = new float[args.Length];

                for (var i = 0; i < args.Length; i++)
                {
                    values[i] = args[i].Evaluate(context);
                }

                return function(values);
            }
        }

        private sealed class ContextFunctionNode(Func<ISmartPropExpressionContext?, float[], float> function, Node[] args) : Node
        {
            public override bool IsConstant => false;

            public override float Evaluate(ISmartPropExpressionContext? context)
            {
                var values = new float[args.Length];

                for (var i = 0; i < args.Length; i++)
                {
                    values[i] = args[i].Evaluate(context);
                }

                return function(context, values);
            }
        }

        private static readonly Dictionary<string, (int Arity, Func<float[], float> Function)> Builtins = new()
        {
            ["sin"] = (1, static a => MathF.Sin(a[0])),
            ["cos"] = (1, static a => MathF.Cos(a[0])),
            ["tan"] = (1, static a => MathF.Tan(a[0])),
            ["asin"] = (1, static a => MathF.Asin(a[0])),
            ["acos"] = (1, static a => MathF.Acos(a[0])),
            ["atan"] = (1, static a => MathF.Atan(a[0])),
            ["sinh"] = (1, static a => MathF.Sinh(a[0])),
            ["cosh"] = (1, static a => MathF.Cosh(a[0])),
            ["tanh"] = (1, static a => MathF.Tanh(a[0])),
            ["frac"] = (1, static a => MathUtils.Fract(a[0])),
            ["saturate"] = (1, static a => MathF.Min(MathF.Max(a[0], 0f), 1f)),
            ["floor"] = (1, static a => MathF.Floor(a[0])),
            ["ceil"] = (1, static a => MathF.Ceiling(a[0])),
            ["round"] = (1, static a => MathF.Round(a[0], MidpointRounding.AwayFromZero)),
            ["log"] = (1, static a => MathF.Log(a[0])),
            ["log2"] = (1, static a => MathF.Log2(a[0])),
            ["log10"] = (1, static a => MathF.Log10(a[0])),
            ["exp"] = (1, static a => MathF.Exp(a[0])),
            ["exp2"] = (1, static a => MathF.Pow(2f, a[0])),
            ["sqrt"] = (1, static a => MathF.Sqrt(a[0])),
            ["rsqrt"] = (1, static a => 1f / MathF.Sqrt(a[0])),
            ["sign"] = (1, static a => a[0] == 0f ? 0f : a[0] > 0f ? 1f : -1f),
            ["abs"] = (1, static a => MathF.Abs(a[0])),
            ["pow"] = (2, static a => MathF.Pow(a[0], a[1])),
            ["min"] = (2, static a => float.MinNumber(a[0], a[1])),
            ["max"] = (2, static a => float.MaxNumber(a[0], a[1])),
            ["clamp"] = (3, static a => a[0] < a[1] ? a[1] : MathF.Min(a[2], a[0])),
            ["lerp"] = (3, static a => (1f - a[2]) * a[0] + a[2] * a[1]),
            ["smoothstep"] = (3, static a =>
            {
                var t = MathF.Min(MathF.Max((a[2] - a[0]) / (a[1] - a[0]), 0f), 1f);
                return t * t * (3f - 2f * t);
            }
            ),
            ["sqr"] = (1, static a => a[0] * a[0]),
            ["deg2rad"] = (1, static a => a[0] * 0.017453292f),
            ["rad2deg"] = (1, static a => a[0] * 57.295776f),
            ["length2d"] = (2, static a => MathF.Sqrt(a[0] * a[0] + a[1] * a[1])),
            ["length3d"] = (3, static a => MathF.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])),
        };

        private static readonly Dictionary<string, (int Arity, Func<ISmartPropExpressionContext?, float[], float> Function)> ContextFunctions = new()
        {
            ["instancecount"] = (0, static (c, _) => c?.InstanceCount ?? 0),
            ["instanceindex"] = (0, static (c, _) => c?.InstanceIndex ?? 0),
            ["evaluationdepth"] = (0, static (c, _) => c?.EvaluationDepth ?? 0),
            ["maxevaluationdepth"] = (0, static (c, _) => c?.MaxEvaluationDepth ?? 0),
            ["pathparameter"] = (0, static (c, _) => c?.PathParameter ?? 0f),
            ["linearscale"] = (0, static (c, _) => c?.LinearScale ?? 1f),
            ["linelength"] = (0, static (c, _) => c?.LineLength ?? 0f),
            ["uscale"] = (0, static (c, _) => c?.UScale ?? 0f),
            ["vscale"] = (0, static (c, _) => c?.VScale ?? 0f),
            ["randomfloat"] = (2, static (c, a) =>
            {
                if (c is null or DictionaryContext)
                {
                    return 0f;
                }

                return a[0] >= a[1] ? a[0] : c.GetRandomStream().RandomFloat(a[0], a[1]);
            }
            ),
            ["randomint"] = (2, static (c, a) =>
            {
                if (c is null or DictionaryContext)
                {
                    return 0f;
                }

                var min = (int)a[0];
                var max = (int)a[1];
                return min >= max ? min : c.GetRandomStream().RandomInt(min, max);
            }
            ),
        };
    }
}
