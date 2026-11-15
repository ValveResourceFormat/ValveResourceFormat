using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.Serialization.VfxEval
{
    /// <summary>
    /// Evaluates and decompiles VFX dynamic expressions.
    /// </summary>
    public class VfxEval
    {
        /// <summary>
        /// Gets the parsed dynamic expression result as a string.
        /// </summary>
        public string DynamicExpressionResult { get; private set; }

        /// <summary>
        /// Gets the original dynamic expression binary blob.
        /// </summary>
        public byte[] DynamicExpressionBlob { get; private set; }

        // the decompiled expression, one statement per line
        private readonly List<string> DynamicExpressionList = [];

        /// <summary>Gets the functions expressions call, indexed by <see cref="ExpressionFunction"/>.</summary>
        public static IReadOnlyList<FunctionAttribute> Functions { get; } = CreateFunctionTable();

        private static FunctionAttribute[] CreateFunctionTable()
        {
            var functions = new FunctionAttribute[Enum.GetValues<ExpressionFunction>().Length];

            foreach (var field in typeof(ExpressionFunction).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                functions[(ushort)field.GetValue(null)!] = field.GetCustomAttribute<FunctionAttribute>()
                    ?? throw new InvalidOperationException($"{field.Name} has no {nameof(FunctionAttribute)}");
            }

            return functions;
        }

        /// <summary>Instructions of compiled dynamic expressions, each followed by its operands.</summary>
        public enum OpCode : byte
        {
            /// <summary>Ends the expression, its result on the stack.</summary>
            Return = 0x00,

            /// <summary>Does nothing.</summary>
            Nop = 0x01,

            /// <summary>Continues at the ushort offset that follows.</summary>
            Jump = 0x02,

            /// <summary>Calls the subroutine at the ushort offset that follows.</summary>
            Call = 0x03,

            /// <summary>Pops a condition and continues at the first ushort offset that follows when it is nonzero, else at the second.</summary>
            Branch = 0x04,

            /// <summary>Returns from a subroutine.</summary>
            Ret = 0x05,

            /// <summary>Calls the <see cref="ExpressionFunction"/> whose ushort index follows.</summary>
            Function = 0x06,

            /// <summary>Pushes the float that follows into every component.</summary>
            Float = 0x07,

            /// <summary>Pops a value into the local whose byte index follows.</summary>
            Store = 0x08,

            /// <summary>Pushes the local whose byte index follows.</summary>
            Load = 0x09,

            /// <summary>Pops two values and pushes 1 per component where either is nonzero, else 0.</summary>
            Or = 0x0A,

            /// <summary>Pops two values and pushes 1 per component where both are nonzero, else 0.</summary>
            And = 0x0B,

            /// <summary>Pops a value and pushes 1 per component where it is zero, else 0.</summary>
            Not = 0x0C,

            /// <summary>Pops two values and pushes 1 per equal component, else 0.</summary>
            Equal = 0x0D,

            /// <summary>Pops two values and pushes 1 per differing component, else 0.</summary>
            NotEqual = 0x0E,

            /// <summary>Pops b, then a, and pushes 1 per component where a &gt; b, else 0.</summary>
            Greater = 0x0F,

            /// <summary>Pops b, then a, and pushes 1 per component where a &gt;= b, else 0.</summary>
            GreaterOrEqual = 0x10,

            /// <summary>Pops b, then a, and pushes 1 per component where a &lt; b, else 0.</summary>
            Less = 0x11,

            /// <summary>Pops b, then a, and pushes 1 per component where a &lt;= b, else 0.</summary>
            LessOrEqual = 0x12,

            /// <summary>Pops b, then a, and pushes a + b.</summary>
            Add = 0x13,

            /// <summary>Pops b, then a, and pushes a - b.</summary>
            Subtract = 0x14,

            /// <summary>Pops b, then a, and pushes a * b.</summary>
            Multiply = 0x15,

            /// <summary>Pops b, then a, and pushes a / b.</summary>
            Divide = 0x16,

            /// <summary>Pops b, then a, and pushes a % b.</summary>
            Modulo = 0x17,

            /// <summary>Pops a value and pushes its negation.</summary>
            Negate = 0x18,

            /// <summary>Pushes the render attribute whose uint token follows.</summary>
            Attribute = 0x19,

            /// <summary>Pushes the feature whose byte index follows.</summary>
            Feature = 0x1A,

            /// <summary>A feature read of unknown operands.</summary>
            Feature2 = 0x1B,

            /// <summary>A feature read of unknown operands.</summary>
            Feature3 = 0x1C,

            /// <summary>Pushes the material parameter whose uint token follows.</summary>
            MaterialParam = 0x1D,

            /// <summary>Pops a value and pushes the components the byte that follows selects, two bits each.</summary>
            Swizzle = 0x1E,

            /// <summary>Pushes 1 when the render attribute whose uint token follows is set, else 0.</summary>
            Exists = 0x1F,

            /// <summary>Pushes the material parameter whose int index follows, mapped to its slot in the material instead of found by token.</summary>
            MaterialParamByIndex = 0x20,

            /// <summary>Pushes the four floats that follow.</summary>
            Float4 = 0x21,

            /// <summary>Pushes the system value whose byte index follows.</summary>
            SystemValue = 0x22,
        }

        /// <summary>Functions an expression calls, by the index the bytecode refers to them with.</summary>
        public enum ExpressionFunction : ushort
        {
            // Named by their FunctionAttribute
#pragma warning disable format, CS1591
            [Function("sin", 1)]                 Sin = 0x00,
            [Function("cos", 1)]                 Cos = 0x01,
            [Function("tan", 1)]                 Tan = 0x02,
            [Function("frac", 1)]                Frac = 0x03,
            [Function("floor", 1)]               Floor = 0x04,
            [Function("ceil", 1)]                Ceil = 0x05,
            [Function("saturate", 1)]            Saturate = 0x06,
            [Function("clamp", 3)]               Clamp = 0x07,
            [Function("lerp", 3)]                Lerp = 0x08,
            [Function("dot4", 2)]                Dot4 = 0x09,
            [Function("dot3", 2)]                Dot3 = 0x0A,
            [Function("dot2", 2)]                Dot2 = 0x0B,
            [Function("log", 1)]                 Log = 0x0C,
            [Function("log2", 1)]                Log2 = 0x0D,
            [Function("log10", 1)]               Log10 = 0x0E,
            [Function("exp", 1)]                 Exp = 0x0F,
            [Function("exp2", 1)]                Exp2 = 0x10,
            [Function("sqrt", 1)]                Sqrt = 0x11,
            [Function("rsqrt", 1)]               Rsqrt = 0x12,
            [Function("sign", 1)]                Sign = 0x13,
            [Function("abs", 1)]                 Abs = 0x14,
            [Function("pow", 2)]                 Pow = 0x15,
            [Function("step", 2)]                Step = 0x16,
            [Function("smoothstep", 3)]          SmoothStep = 0x17,
            [Function("float4", 4)]              Float4 = 0x18,
            [Function("float3", 3)]              Float3 = 0x19,
            [Function("float2", 2)]              Float2 = 0x1A,
            [Function("time", 0)]                Time = 0x1B,
            [Function("min", 2)]                 Min = 0x1C,
            [Function("max", 2)]                 Max = 0x1D,
            [Function("SrgbLinearToGamma", 1)]   SrgbLinearToGamma = 0x1E,
            [Function("SrgbGammaToLinear", 1)]   SrgbGammaToLinear = 0x1F,
            [Function("random", 2)]              Random = 0x20,
            [Function("normalize", 1)]           Normalize = 0x21,
            [Function("length", 1)]              Length = 0x22,
            [Function("sqr", 1)]                 Sqr = 0x23,
            [Function("rotation2d", 1)]          Rotation2d = 0x24,
            [Function("rotate2d", 2)]            Rotate2d = 0x25,
            [Function("sincos", 1)]              SinCos = 0x26,
            [Function("TextureSize", 1)]         TextureSize = 0x27,
            [Function("TextureAverageColor", 1)] TextureAverageColor = 0x28,
            [Function("MatrixIdentity", 0)]      MatrixIdentity = 0x29,
            [Function("MatrixScale", 1)]         MatrixScale = 0x2A,
            [Function("MatrixTranslate", 1)]     MatrixTranslate = 0x2B,
            [Function("MatrixAxisAngle", 1)]     MatrixAxisAngle = 0x2C,
            [Function("MatrixAxisToAxis", 2)]    MatrixAxisToAxis = 0x2D,
            [Function("MatrixMultiply", 2)]      MatrixMultiply = 0x2E,
            [Function("MatrixColorCorrect", 1)]  MatrixColorCorrect = 0x2F,
            [Function("MatrixColorCorrect2", 2)] MatrixColorCorrect2 = 0x30,
            [Function("MatrixColorTint", 1)]     MatrixColorTint = 0x31,
            [Function("normalize_safe", 1)]      NormalizeSafe = 0x32,
            [Function("Remap01ScaleOffset", 1)]  Remap01ScaleOffset = 0x33,
            [Function("radians", 1)]             Radians = 0x34,
            [Function("degrees", 1)]             Degrees = 0x35,
            [Function("MatrixColorTint2", 2)]    MatrixColorTint2 = 0x36,
            [Function("MatrixColorTint3", 3)]    MatrixColorTint3 = 0x37,
            [Function("RemapVal", 5)]            RemapVal = 0x38,
            [Function("RemapValClamped", 5)]     RemapValClamped = 0x39,
            [Function("MakeOrthonormalRight", 1)]   MakeOrthonormalRight = 0x3A,
            [Function("MakeOrthonormalForward", 1)] MakeOrthonormalForward = 0x3B,
#pragma warning restore format, CS1591
        }

        /// <summary>Names an <see cref="ExpressionFunction"/> as expressions write it, with its number of arguments.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="argumentCount">The number of arguments.</param>
        [AttributeUsage(AttributeTargets.Field)]
        public sealed class FunctionAttribute(string name, int argumentCount) : Attribute
        {
            /// <summary>Gets the function name.</summary>
            public string Name { get; } = name;

            /// <summary>Gets the number of arguments.</summary>
            public int ArgumentCount { get; } = argumentCount;
        }

        // Binding strength, loosest first. An operand is bracketed only where it binds looser than
        // the operator using it, so the output has no brackets the reader would infer anyway.
        private enum Precedence
        {
            Conditional,    // a ? b : c
            Or,             // a || b
            And,            // a && b
            Equality,       // a == b
            Relational,     // a < b
            Additive,       // a + b
            Multiplicative, // a * b
            Unary,          // -a
            Atom,           // literals, names, calls, swizzles
        }

        // Comparisons, &&, || and ?: yield booleans and bind looser than this. Nested in a numeric
        // operator, or as a conditional's condition or true result, they keep brackets precedence would drop:
        // (a==b) ? x : y rather than a==b ? x : y, and (a==b)!=(c>=d) rather than a==b!=c>=d.
        private const Precedence AboveBoolean = Precedence.Additive;

        private static (string Symbol, Precedence Precedence) GetOperator(OpCode op) => op switch
        {
            OpCode.Or => ("||", Precedence.Or),
            OpCode.And => ("&&", Precedence.And),
            OpCode.Equal => ("==", Precedence.Equality),
            OpCode.NotEqual => ("!=", Precedence.Equality),
            OpCode.Greater => (">", Precedence.Relational),
            OpCode.GreaterOrEqual => (">=", Precedence.Relational),
            OpCode.Less => ("<", Precedence.Relational),
            OpCode.LessOrEqual => ("<=", Precedence.Relational),
            OpCode.Add => ("+", Precedence.Additive),
            OpCode.Subtract => ("-", Precedence.Additive),
            OpCode.Multiply => ("*", Precedence.Multiplicative),
            OpCode.Divide => ("/", Precedence.Multiplicative),
            OpCode.Modulo => ("%", Precedence.Multiplicative),
            OpCode.Not => ("!", Precedence.Unary),
            OpCode.Negate => ("-", Precedence.Unary),
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

        // A branch writes both of its blocks inline, one of them directly following the branch.
        // Which one that is tells the two conditional forms apart: <cond> ? <e1> : <e2> always
        // writes the block taken when the condition holds first, <e1> && <e2> writes the other one first.
        private const uint TRUE_BLOCK_FIRST = 0;
        private const uint FALSE_BLOCK_FIRST = 1;

        // Each expression has two renderings, since the consumer is unknown when it is built.
        // Text is used anywhere in the tree. ResultText is used where the value becomes the value of
        // the whole expression, the only place literals name a render state value.
        private readonly struct Expression(string text, string resultText, Precedence precedence)
        {
            public string Text { get; } = text;
            public string ResultText { get; } = resultText;
            public Precedence Precedence { get; } = precedence;

            public string Operand(Precedence required) => Precedence < required ? $"({Text})" : Text;
            public string ResultOperand(Precedence required) => Precedence < required ? $"({ResultText})" : ResultText;
        }

        private readonly Stack<Expression> Expressions = new();

        // pairs of (exit offset, block order) for open branches, pushed by BRANCH and JUMP
        // and combined into a conditional once the exit offset is reached
        private readonly Stack<uint> OffsetAtBranchExits = new();
        private readonly Dictionary<uint, string> LocalVariableNames = [];

        // Names as spelled in the expression's material or shader, the built-in string token table is only a fallback
        private readonly Dictionary<uint, string> TokenNames = [];

        /// <summary>
        /// Gets the list of render attributes used in the expression.
        /// </summary>
        public IReadOnlyList<string> RenderAttributesUsed { get; }

        /// <summary>
        /// Gets the enum mapper function.
        /// </summary>
        public Func<int, string>? EnumMapper { get; }

        // The 'return' keyword in the last line of a dynamic expression is optional (it is implied where absent)
        // OmitReturnStatement controls whether it is shown
        private readonly bool OmitReturnStatement;

        private readonly IReadOnlyList<string>? Features;

        /// <summary>
        /// Initializes a new instance of the <see cref="VfxEval"/> class.
        /// </summary>
        /// <param name="binaryBlob">The binary blob to parse.</param>
        /// <param name="omitReturnStatement">Whether to omit the return statement in the output.</param>
        /// <param name="features">The list of features.</param>
        /// <param name="enumMapper">The enum mapper function.</param>
        /// <param name="knownNames">Names the expression's attributes and parameters may have, such as the variables of its shader.</param>
        public VfxEval(byte[] binaryBlob, bool omitReturnStatement = false, IReadOnlyList<string>? features = null, Func<int, string>? enumMapper = null,
            IEnumerable<string>? knownNames = null)
            : this(binaryBlob, [], omitReturnStatement, features, enumMapper, knownNames)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VfxEval"/> class.
        /// </summary>
        /// <param name="binaryBlob">The binary blob to parse.</param>
        /// <param name="renderAttributesUsed">The list of render attributes used.</param>
        /// <param name="omitReturnStatement">Whether to omit the return statement in the output.</param>
        /// <param name="features">The list of features.</param>
        /// <param name="enumMapper">The enum mapper function.</param>
        /// <param name="knownNames">Names the expression's attributes and parameters may have besides <paramref name="renderAttributesUsed"/>, such as those of its material.</param>
        public VfxEval(byte[] binaryBlob, IReadOnlyList<string> renderAttributesUsed,
            bool omitReturnStatement = false,
            IReadOnlyList<string>? features = null,
            Func<int, string>? enumMapper = null,
            IEnumerable<string>? knownNames = null)
        {
            OmitReturnStatement = omitReturnStatement;
            Features = features;
            EnumMapper = enumMapper;
            RenderAttributesUsed = renderAttributesUsed;

            foreach (var name in renderAttributesUsed.Concat(knownNames ?? []))
            {
                TokenNames.TryAdd(StringToken.Get(name), name);

                if (VfxVariableDescription.GetNameWithoutTypePrefix(name) is { } unprefixedName)
                {
                    TokenNames.TryAdd(StringToken.Get(unprefixedName), unprefixedName);
                }
            }

            ParseExpression(binaryBlob);
        }

        [MemberNotNull(nameof(DynamicExpressionBlob), nameof(DynamicExpressionResult))]
        private void ParseExpression(byte[] binaryBlob)
        {
            DynamicExpressionBlob = binaryBlob;

            using var dataReader = new BinaryReader(new MemoryStream(binaryBlob));

            while (dataReader.BaseStream.Position < binaryBlob.Length)
            {
                ProcessOps((OpCode)dataReader.ReadByte(), dataReader);
            }

            DynamicExpressionResult = string.Join("\n", DynamicExpressionList);
        }

        private void ProcessOps(OpCode op, BinaryReader dataReader)
        {
            CombineFinishedBranches(dataReader);

            switch (op)
            {
                case OpCode.Jump:
                    // the jump terminating the first block of a branch reveals where the branch exits
                    OffsetAtBranchExits.Push(dataReader.ReadUInt16() + 1u);
                    return;

                // Version 58 shaders compile both branch blocks as subroutines after the main code, called by BRANCH and ending in RET
                case OpCode.Branch:
                {
                    var pointerWhenTrue = dataReader.ReadUInt16();
                    dataReader.ReadUInt16(); // pointer taken when the condition fails

                    OffsetAtBranchExits.Push(pointerWhenTrue == dataReader.BaseStream.Position ? TRUE_BLOCK_FIRST : FALSE_BLOCK_FIRST);
                    return;
                }

                case OpCode.Function:
                {
                    var funcId = dataReader.ReadByte();
                    var funcCheckByte = dataReader.ReadByte();

                    if (funcId >= Functions.Count)
                    {
                        throw new InvalidDataException($"Error parsing dynamic expression, invalid function Id = 0x{funcId:x} (position: {dataReader.BaseStream.Position})");
                    }

                    if (funcCheckByte != 0)
                    {
                        throw new InvalidDataException($"Error parsing dynamic expression, malformed function signature (position: {dataReader.BaseStream.Position})");
                    }

                    var function = Functions[funcId];
                    var funcName = function.Name;
                    var nrArguments = function.ArgumentCount;
                    var arguments = new string[nrArguments];

                    for (var i = nrArguments - 1; i >= 0; i--)
                    {
                        arguments[i] = PopExpression(dataReader).Text;
                    }

                    Push($"{funcName}({string.Join(',', arguments)})");
                    return;
                }

                case OpCode.Float:
                {
                    var floatLiteral = dataReader.ReadSingle().ToString("g", CultureInfo.InvariantCulture);

                    // if a float leads with "0." remove the 0 (as how Valve likes it)
                    var literal = floatLiteral.StartsWith("0.", StringComparison.Ordinal) ? floatLiteral[1..] : floatLiteral;
                    Expressions.Push(new Expression(literal, TryGetValueName(literal, out var name) ? name : literal, Precedence.Atom));
                    return;
                }

                // assignment is always to a local variable, and it terminates the line
                case OpCode.Store:
                {
                    var locVarname = GetLocalVarName(dataReader.ReadByte());
                    DynamicExpressionList.Add($"{locVarname} = {PopExpression(dataReader).Text};");
                    return;
                }

                case OpCode.Load:
                    Push(GetLocalVarName(dataReader.ReadByte()));
                    return;

                case OpCode.Not:
                case OpCode.Negate:
                {
                    var operand = PopExpression(dataReader).Operand(Precedence.Unary);

                    // Negating a negative operand would otherwise print as a decrement
                    if (op == OpCode.Negate && operand.StartsWith('-', StringComparison.Ordinal))
                    {
                        operand = $"({operand})";
                    }

                    Push($"{GetOperator(op).Symbol}{operand}", Precedence.Unary);
                    return;
                }

                // Both operands are evaluated, unlike the branches || and && usually compile to
                case OpCode.Or or OpCode.And:
                {
                    var (symbol, precedence) = GetOperator(op);
                    var exp2 = PopExpression(dataReader);
                    var exp1 = PopExpression(dataReader);

                    Push($"{exp1.Operand(precedence)} {symbol} {exp2.Operand(precedence + 1)}", precedence);
                    return;
                }

                case >= OpCode.Equal and <= OpCode.Modulo:
                {
                    var (symbol, precedence) = GetOperator(op);
                    var exp2 = PopExpression(dataReader);
                    var exp1 = PopExpression(dataReader);

                    // the bytecode is left nested, so an operand of the same precedence on the right
                    // was bracketed in the source and stays bracketed: 1-(2-3) is not 1-2-3
                    var (left, right) = precedence < AboveBoolean
                        ? (AboveBoolean, AboveBoolean) // a comparison, both of its operands are numbers
                        : (precedence, precedence + 1);

                    Push($"{exp1.Operand(left)}{symbol}{exp2.Operand(right)}", precedence);
                    return;
                }

                case OpCode.Attribute:
                    Push(ReadTokenName(dataReader, "ATTRIBUTE"));
                    return;

                case OpCode.MaterialParam:
                    Push(ReadTokenName(dataReader, "MATERIAL_PARAM"));
                    return;

                case OpCode.Exists:
                    Push($"exists({ReadTokenName(dataReader, "ATTRIBUTE")})");
                    return;

                case OpCode.Feature:
                {
                    uint featureId = dataReader.ReadByte();
                    Push(Features is not null && featureId < Features.Count
                        ? Features[(int)featureId]
                        : $"FEAT[{featureId}]");
                    return;
                }

                case OpCode.SystemValue:
                {
                    var index = dataReader.ReadByte();
                    var sourceType = (VfxStaticComboSourceType)VfxCombo.NormalizeStaticComboSourceType(index);
                    Push(Enum.IsDefined(sourceType) ? sourceType.ToString() : $"SYS[{index}]");
                    return;
                }

                case OpCode.Swizzle:
                {
                    var exp = PopExpression(dataReader).Operand(Precedence.Atom);
                    Push($"{exp}.{GetSwizzle(dataReader.ReadByte())}");
                    return;
                }

                // parser terminates here
                case OpCode.Return:
                {
                    if (dataReader.BaseStream.Position < dataReader.BaseStream.Length)
                    {
                        throw new InvalidDataException($"Looks like we did not read the data correctly (position: {dataReader.BaseStream.Position})");
                    }

                    var finalExp = PopExpression(dataReader).ResultText;
                    DynamicExpressionList.Add(OmitReturnStatement ? finalExp : $"return {finalExp};");
                    return;
                }

                default:
                    throw new InvalidDataException($"Error parsing dynamic expression, unknown opcode = 0x{(int)op:x2} (position: {dataReader.BaseStream.Position})");
            }
        }

        // when exiting a branch, combine the conditional expressions on the stack into one
        private void CombineFinishedBranches(BinaryReader dataReader)
        {
            while (OffsetAtBranchExits.Count > 0
                && OffsetAtBranchExits.Peek() == dataReader.BaseStream.Position)
            {
                OffsetAtBranchExits.Pop();
                var trueBlockFirst = OffsetAtBranchExits.Pop() == TRUE_BLOCK_FIRST;

                var expSecondBlock = PopExpression(dataReader);
                var expFirstBlock = PopExpression(dataReader);
                var expCondition = PopExpression(dataReader);

                var (whenTrue, whenFalse) = trueBlockFirst
                    ? (expFirstBlock, expSecondBlock)
                    : (expSecondBlock, expFirstBlock);

                // && and || fold to a constant when they short circuit, and the shape alone tells which to restore.
                // Results of exactly 1 and 0 fit both forms, and there the conditional is what the source read as.
                string Fold(string symbol, Precedence precedence, in Expression other)
                    => $"{expCondition.Operand(precedence)} {symbol} {other.Operand(precedence + 1)}";

                var shortCircuit = whenTrue.Text == "1" && whenFalse.Text == "0" ? null
                    : !trueBlockFirst && whenFalse.Text == "0" ? Fold("&&", Precedence.And, whenTrue)
                    : trueBlockFirst && whenTrue.Text == "1" ? Fold("||", Precedence.Or, whenFalse)
                    : null;

                // A conditional choosing between two values is the same bytes as a fold.
                // When both results name a value, restore the conditional in result position and the fold elsewhere.
                var namedResults = TryGetValueName(whenTrue.Text, out _) && TryGetValueName(whenFalse.Text, out _);

                // A conditional nested in the false result chains into a readable series of cases; the
                // true result sits between the '?' and the ':' and brackets like a condition does.
                var conditional = $"{expCondition.Operand(AboveBoolean)} ? {whenTrue.Operand(AboveBoolean)} : {whenFalse.Text}";

                Expressions.Push(shortCircuit is not null && !namedResults
                    ? new Expression(shortCircuit, shortCircuit, trueBlockFirst ? Precedence.Or : Precedence.And)
                    : new Expression(shortCircuit ?? conditional,
                        $"{expCondition.Operand(AboveBoolean)} ? {whenTrue.ResultOperand(AboveBoolean)} : {whenFalse.ResultText}",
                        Precedence.Conditional));
            }
        }

        private void Push(string expression, Precedence precedence = Precedence.Atom)
        {
            Expressions.Push(new Expression(expression, expression, precedence));
        }

        private Expression PopExpression(BinaryReader dataReader)
        {
            if (!Expressions.TryPop(out var exp))
            {
                throw new InvalidDataException($"Error parsing dynamic expression, expression stack is empty (position: {dataReader.BaseStream.Position})");
            }

            return exp;
        }

        private string ReadTokenName(BinaryReader dataReader, string unknownPrefix)
        {
            var token = dataReader.ReadUInt32();
            return TokenNames.GetValueOrDefault(token) ?? StringToken.BuiltInTable.GetValueOrDefault(token) ?? $"{unknownPrefix}[{token:x08}]";
        }

        // A literal only names a value where an expression's value becomes the value of the whole
        // expression: the returned expression, and the two results of a conditional. Everywhere else
        // (conditions, comparisons, arithmetic, function arguments) it is a plain number.
        private bool TryGetValueName(string exp, [NotNullWhen(true)] out string? name)
        {
            if (EnumMapper != null && int.TryParse(exp, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                name = EnumMapper(value);
                return name != exp;
            }

            name = null;
            return false;
        }

        private static string GetSwizzle(byte packedSwizzle, bool trimmed = true)
        {
            const int MaxLength = 4;
            Span<char> chars = stackalloc char[MaxLength];
            Span<char> axes = ['x', 'y', 'z', 'w'];

            for (var i = 0; i < MaxLength; i++)
            {
                chars[i] = axes[(packedSwizzle >> (i * 2)) & 3];
            }

            var length = MaxLength;
            while (trimmed && length > 1 && chars[length - 1] == chars[length - 2])
            {
                length--;
            }

            return chars[..length].ToString();
        }

        // naming local variables v0,v1,v2,..
        private string GetLocalVarName(uint varId)
        {
            if (!LocalVariableNames.TryGetValue(varId, out var varName))
            {
                varName = $"v{LocalVariableNames.Count}";
                LocalVariableNames.Add(varId, varName);
            }

            return varName;
        }
    }
}
