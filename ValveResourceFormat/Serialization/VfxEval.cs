using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;

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

        // function reference, name and number of arguments
        private static readonly (string Name, int ArgumentCount)[] FUNCTION_REF = [
#pragma warning disable format
            ("sin",        1),     // 00
            ("cos",        1),     // 01
            ("tan",        1),     // 02
            ("frac",       1),     // 03
            ("floor",      1),     // 04
            ("ceil",       1),     // 05
            ("saturate",   1),     // 06
            ("clamp",      3),     // 07
            ("lerp",       3),     // 08
            ("dot4",       2),     // 09
            ("dot3",       2),     // 0A
            ("dot2",       2),     // 0B
            ("log",        1),     // 0C
            ("log2",       1),     // 0D
            ("log10",      1),     // 0E
            ("exp",        1),     // 0F
            ("exp2",       1),     // 10
            ("sqrt",       1),     // 11
            ("rsqrt",      1),     // 12
            ("sign",       1),     // 13
            ("abs",        1),     // 14
            ("pow",        2),     // 15
            ("step",       2),     // 16
            ("smoothstep", 3),     // 17
            ("float4",     4),     // 18
            ("float3",     3),     // 19
            ("float2",     2),     // 1A
            ("time",       0),     // 1B
            ("min",        2),     // 1C
            ("max",        2),     // 1D
            ("SrgbLinearToGamma",1), // 1E
            ("SrgbGammaToLinear",1), // 1F
            ("random",     2),     // 20
            ("normalize",  1),     // 21
            ("length",     1),     // 22
            ("sqr",        1),     // 23
            ("rotation2d", 1),     // 24
            ("rotate2d",   2),     // 25
            ("sincos",     1),     // 26
            ("TextureSize",1),     // 27
            ("TextureAverageColor", 1), // 28
            ("MatrixIdentity",      0), // 29
            ("MatrixScale",         1), // 2A
            ("MatrixTranslate",     1), // 2B
            ("MatrixAxisAngle",     1), // 2C
            ("MatrixAxisToAxis",    2), // 2D
            ("MatrixMultiply",      2), // 2E
            ("MatrixColorCorrect",  1), // 2F
            ("MatrixColorCorrect2", 2), // 30
            ("MatrixColorTint",     1), // 31
            ("normalize_safe",      1), // 32
            ("Remap01ScaleOffset",  1), // 33
            ("radians",             1), // 34
            ("degrees",             1), // 35
            ("MatrixColorTint2",    2), // 36
            ("MatrixColorTint3",    3), // 37
            ("RemapVal",            5), // 38
            ("RemapValClamped",     5), // 39
#pragma warning restore format
        ];

        private enum OPCODE
        {
            RETURN,             // 00
            NOP,                // 01
            JUMP,               // 02
            CALL,               // 03
            BRANCH,             // 04
            RET,                // 05
            FUNC,               // 06
            FLOAT,              // 07
            STORE,              // 08
            LOAD,               // 09
            OR,                 // 0A
            AND,                // 0B
            NOT,                // 0C
            EQUALS,             // 0D
            NEQUALS,            // 0E
            GT,                 // 0F
            GTE,                // 10
            LT,                 // 11
            LTE,                // 12
            ADD,                // 13
            SUB,                // 14
            MUL,                // 15
            DIV,                // 16
            MODULO,             // 17
            NEGATE,             // 18
            ATTRIBUTE,          // 19
            FEATURE,            // 1A
            FEATURE2,           // 1B
            FEATURE3,           // 1C
            MATERIAL_PARAM,     // 1D
            SWIZZLE,            // 1E
            EXISTS,             // 1F
            MATERIAL_PARAM_IDX, // 20
            FLOAT4,             // 21
        };

        // How tightly an expression binds, listed loosest first. An operand is bracketed only where
        // it binds looser than the operator using it, which keeps the output free of brackets that
        // the reader would put back in the same place anyway.
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

        // Comparisons, &&, || and ?: are the operators producing a boolean, and they all bind looser
        // than this. Where one of them is nested in an operator taking numbers, or in the condition or
        // true result of a conditional, it keeps the brackets precedence would let us drop:
        // (a==b) ? x : y reads better than a==b ? x : y, and (a==b)!=(c>=d) better than a==b!=c>=d.
        private const Precedence AboveBoolean = Precedence.Additive;

        private static (string Symbol, Precedence Precedence) GetOperator(OPCODE op) => op switch
        {
            OPCODE.EQUALS => ("==", Precedence.Equality),
            OPCODE.NEQUALS => ("!=", Precedence.Equality),
            OPCODE.GT => (">", Precedence.Relational),
            OPCODE.GTE => (">=", Precedence.Relational),
            OPCODE.LT => ("<", Precedence.Relational),
            OPCODE.LTE => ("<=", Precedence.Relational),
            OPCODE.ADD => ("+", Precedence.Additive),
            OPCODE.SUB => ("-", Precedence.Additive),
            OPCODE.MUL => ("*", Precedence.Multiplicative),
            OPCODE.DIV => ("/", Precedence.Multiplicative),
            OPCODE.MODULO => ("%", Precedence.Multiplicative),
            OPCODE.NOT => ("!", Precedence.Unary),
            OPCODE.NEGATE => ("-", Precedence.Unary),
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

        // A branch writes both of its blocks inline, one of them directly following the branch.
        // Which one that is tells the two conditional forms apart: <cond> ? <e1> : <e2> always
        // writes the block taken when the condition holds first, <e1> && <e2> writes the other one first.
        private const uint TRUE_BLOCK_FIRST = 0;
        private const uint FALSE_BLOCK_FIRST = 1;

        // An expression carries two renderings, because at the point it is built it is not yet known
        // how it is consumed. Text is what it reads as anywhere in the expression tree, ResultText is
        // what it reads as where its value becomes the value of the whole expression, which is the
        // only place where literals name a render state value.
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
        public VfxEval(byte[] binaryBlob, bool omitReturnStatement = false, IReadOnlyList<string>? features = null, Func<int, string>? enumMapper = null)
            : this(binaryBlob, [], omitReturnStatement, features, enumMapper)
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
        public VfxEval(byte[] binaryBlob, IReadOnlyList<string> renderAttributesUsed,
            bool omitReturnStatement = false,
            IReadOnlyList<string>? features = null,
            Func<int, string>? enumMapper = null)
        {
            OmitReturnStatement = omitReturnStatement;
            Features = features;
            EnumMapper = enumMapper;
            RenderAttributesUsed = renderAttributesUsed;

            StringToken.Store(renderAttributesUsed);
            ParseExpression(binaryBlob);
        }

        [MemberNotNull(nameof(DynamicExpressionBlob), nameof(DynamicExpressionResult))]
        private void ParseExpression(byte[] binaryBlob)
        {
            DynamicExpressionBlob = binaryBlob;

            using var dataReader = new BinaryReader(new MemoryStream(binaryBlob));

            while (dataReader.BaseStream.Position < binaryBlob.Length)
            {
                ProcessOps((OPCODE)dataReader.ReadByte(), dataReader);
            }

            DynamicExpressionResult = string.Join("\n", DynamicExpressionList);
        }

        private void ProcessOps(OPCODE op, BinaryReader dataReader)
        {
            CombineFinishedBranches(dataReader);

            switch (op)
            {
                case OPCODE.JUMP:
                    // the jump terminating the first block of a branch reveals where the branch exits
                    OffsetAtBranchExits.Push(dataReader.ReadUInt16() + 1u);
                    return;

                case OPCODE.BRANCH:
                    {
                        var pointerWhenTrue = dataReader.ReadUInt16();
                        dataReader.ReadUInt16(); // pointer taken when the condition fails

                        OffsetAtBranchExits.Push(pointerWhenTrue == dataReader.BaseStream.Position ? TRUE_BLOCK_FIRST : FALSE_BLOCK_FIRST);
                        return;
                    }

                case OPCODE.FUNC:
                    {
                        var funcId = dataReader.ReadByte();
                        var funcCheckByte = dataReader.ReadByte();

                        if (funcId >= FUNCTION_REF.Length)
                        {
                            throw new InvalidDataException($"Error parsing dynamic expression, invalid function Id = 0x{funcId:x} (position: {dataReader.BaseStream.Position})");
                        }

                        if (funcCheckByte != 0)
                        {
                            throw new InvalidDataException($"Error parsing dynamic expression, malformed function signature (position: {dataReader.BaseStream.Position})");
                        }

                        var (funcName, nrArguments) = FUNCTION_REF[funcId];
                        var arguments = new string[nrArguments];

                        for (var i = nrArguments - 1; i >= 0; i--)
                        {
                            arguments[i] = PopExpression(dataReader).Text;
                        }

                        Push($"{funcName}({string.Join(',', arguments)})");
                        return;
                    }

                case OPCODE.FLOAT:
                    {
                        var floatLiteral = dataReader.ReadSingle().ToString("g", CultureInfo.InvariantCulture);

                        // if a float leads with "0." remove the 0 (as how Valve likes it)
                        var literal = floatLiteral.StartsWith("0.", StringComparison.Ordinal) ? floatLiteral[1..] : floatLiteral;
                        Expressions.Push(new Expression(literal, TryGetValueName(literal, out var name) ? name : literal, Precedence.Atom));
                        return;
                    }

                // assignment is always to a local variable, and it terminates the line
                case OPCODE.STORE:
                    {
                        var locVarname = GetLocalVarName(dataReader.ReadByte());
                        DynamicExpressionList.Add($"{locVarname} = {PopExpression(dataReader).Text};");
                        return;
                    }

                case OPCODE.LOAD:
                    Push(GetLocalVarName(dataReader.ReadByte()));
                    return;

                case OPCODE.NOT:
                case OPCODE.NEGATE:
                    Push($"{GetOperator(op).Symbol}{PopExpression(dataReader).Operand(Precedence.Unary)}", Precedence.Unary);
                    return;

                case >= OPCODE.EQUALS and <= OPCODE.MODULO:
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

                case OPCODE.ATTRIBUTE:
                    Push(ReadTokenName(dataReader, "ATTRIBUTE"));
                    return;

                case OPCODE.MATERIAL_PARAM:
                    Push(ReadTokenName(dataReader, "MATERIAL_PARAM"));
                    return;

                case OPCODE.EXISTS:
                    Push($"exists({ReadTokenName(dataReader, "ATTRIBUTE")})");
                    return;

                case OPCODE.FEATURE:
                    {
                        uint featureId = dataReader.ReadByte();
                        Push(Features is not null && featureId < Features.Count
                            ? Features[(int)featureId]
                            : $"FEAT[{featureId}]");
                        return;
                    }

                case OPCODE.SWIZZLE:
                    {
                        var exp = PopExpression(dataReader).Operand(Precedence.Atom);
                        Push($"{exp}.{GetSwizzle(dataReader.ReadByte())}");
                        return;
                    }

                // parser terminates here
                case OPCODE.RETURN:
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

                // && and || evaluate to a constant when they short circuit, restore their source form.
                // Which one it was is decided by the shape alone. Results of exactly 1 and 0 are the one
                // shape the two forms share, and there the conditional is what the source read as.
                string Fold(string symbol, Precedence precedence, in Expression other)
                    => $"{expCondition.Operand(precedence)} {symbol} {other.Operand(precedence + 1)}";

                var shortCircuit = whenTrue.Text == "1" && whenFalse.Text == "0" ? null
                    : !trueBlockFirst && whenFalse.Text == "0" ? Fold("&&", Precedence.And, whenTrue)
                    : trueBlockFirst && whenTrue.Text == "1" ? Fold("||", Precedence.Or, whenFalse)
                    : null;

                // A conditional picking between two values is written as the same bytes, so where both
                // results are constants that name a value, the conditional is what the expression reads
                // as in result position, and the fold is only how it reads everywhere else.
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

        private static string ReadTokenName(BinaryReader dataReader, string unknownPrefix)
        {
            var token = dataReader.ReadUInt32();
            return StringToken.InvertedTable.GetValueOrDefault(token, $"{unknownPrefix}[{token:x08}]");
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
