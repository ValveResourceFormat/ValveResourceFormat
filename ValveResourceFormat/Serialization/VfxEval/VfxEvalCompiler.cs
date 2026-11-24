using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using OpCode = ValveResourceFormat.Serialization.VfxEval.VfxEval.OpCode;

namespace ValveResourceFormat.Serialization.VfxEval
{
    /// <summary>Compiles expression text for <see cref="VfxEvalRuntime"/>.</summary>
    /// <remarks>
    /// Takes function definitions, then <c>v0 = ...;</c> locals, then an expression of calls, names, numbers,
    /// operators, swizzles and <c>?:</c>, optionally after <c>return</c>. A function has typed parameters, locals and
    /// one <c>return</c>, and is inlined at each call.
    /// </remarks>
    public sealed class VfxEvalCompiler
    {
        // Binary operators by precedence, two-character symbols first; || and && compile to branches
        private static readonly (string Symbol, OpCode? Opcode, int Precedence)[] BinaryOperators =
        [
            ("||", null, 1),
            ("&&", null, 2),
            ("==", OpCode.Equal, 3),
            ("!=", OpCode.NotEqual, 3),
            (">=", OpCode.GreaterOrEqual, 4),
            ("<=", OpCode.LessOrEqual, 4),
            (">", OpCode.Greater, 4),
            ("<", OpCode.Less, 4),
            ("+", OpCode.Add, 5),
            ("-", OpCode.Subtract, 5),
            ("*", OpCode.Multiply, 6),
            ("/", OpCode.Divide, 6),
            ("%", OpCode.Modulo, 6),
        ];

        // The parameter names and the offset of the body after its opening brace
        private readonly record struct UserFunction(string[] Parameters, int Body);

        private readonly string source;
        private readonly List<byte> code = [];
        private readonly Dictionary<string, UserFunction> functions = [];
        private readonly HashSet<string> expanding = [];
        private Dictionary<string, byte> locals = [];
        private int nextSlot;
        private int position;

        private VfxEvalCompiler(string source)
        {
            this.source = source;
        }

        /// <summary>Compiles an expression, throwing <see cref="InvalidDataException"/> when it is not valid.</summary>
        public static byte[] Compile(string source)
        {
            var compiler = new VfxEvalCompiler(source);
            compiler.ParseFunctionDefinitions();
            compiler.ParseStatements();

            if (compiler.Peek() != '\0')
            {
                throw compiler.Error($"Unexpected '{compiler.Peek()}'");
            }

            compiler.code.Add((byte)OpCode.Return);
            return [.. compiler.code];
        }

        private void ParseFunctionDefinitions()
        {
            while (true)
            {
                var start = position;
                var type = TryParseIdentifier();
                var name = type is null or "return" ? null : TryParseIdentifier();

                if (name == null || !TryConsume('('))
                {
                    position = start;
                    return;
                }

                if (FindFunction(name) >= 0 || functions.ContainsKey(name))
                {
                    throw Error($"Function '{name}' is already defined");
                }

                List<string> parameters = [];

                if (!TryConsume(')'))
                {
                    do
                    {
                        if (TryParseIdentifier() == null || TryParseIdentifier() is not { } parameter)
                        {
                            throw Error("Expected a parameter type and name");
                        }

                        parameters.Add(parameter);
                    }
                    while (TryConsume(','));

                    Expect(')');
                }

                Expect('{');
                functions.Add(name, new UserFunction([.. parameters], position));

                var end = source.IndexOf('}', position, StringComparison.Ordinal);
                position = end >= 0 ? end + 1 : throw Error($"Function '{name}' has no closing brace");
            }
        }

        private void ParseStatements()
        {
            while (true)
            {
                while (TryConsume(';'))
                {
                }

                var start = position;
                var name = TryParseIdentifier();

                if (name == "return")
                {
                    break;
                }

                // A type before the name is allowed and ignored
                if (name != null && (char.IsAsciiLetter(Peek()) || Peek() == '_'))
                {
                    name = TryParseIdentifier();
                }

                if (name == null || Peek() != '=' || PeekNext() == '=')
                {
                    position = start;
                    break;
                }

                position++;
                ParseExpression();
                Expect(';');

                if (!locals.TryGetValue(name, out var slot))
                {
                    slot = AllocateSlot();
                    locals.Add(name, slot);
                }

                code.Add((byte)OpCode.Store);
                code.Add(slot);
            }

            ParseExpression();
            TryConsume(';');
        }

        private void ParseExpression()
        {
            ParseBinary(1);

            if (!TryConsume('?'))
            {
                return;
            }

            var falseTarget = WriteBranch(trueBlockFirst: true);
            ParseExpression();
            Expect(':');

            var jump = WriteJump();
            WriteOffset(falseTarget);
            ParseExpression();
            WriteOffset(jump);
        }

        // Writes a branch to the block right after it, and returns where the other target goes
        private int WriteBranch(bool trueBlockFirst)
        {
            code.Add((byte)OpCode.Branch);
            var whenTrue = code.Count;
            var whenFalse = whenTrue + 2;
            code.AddRange(new byte[4]);
            WriteOffset(trueBlockFirst ? whenTrue : whenFalse);
            return trueBlockFirst ? whenFalse : whenTrue;
        }

        // Writes a jump past the second block of a branch, and returns the offset of its target
        private int WriteJump()
        {
            code.Add((byte)OpCode.Jump);
            var jump = code.Count;
            code.AddRange(new byte[2]);
            return jump;
        }

        private void WriteFloat(float value)
        {
            code.Add((byte)OpCode.Float);
            Write(BitConverter.SingleToUInt32Bits(value));
        }

        // Points a placeholder at the next instruction
        private void WriteOffset(int at) => BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(code)[at..], (ushort)code.Count);

        private void ParseBinary(int minPrecedence)
        {
            ParseUnary();

            while (TryConsumeOperator(minPrecedence, out var symbol, out var opcode, out var precedence))
            {
                if (opcode is { } binary)
                {
                    ParseBinary(precedence + 1);
                    code.Add((byte)binary);
                    continue;
                }

                // a || b is a ? 1 : b, and a && b is a ? b : 0 with the false block first
                var isOr = symbol == "||";
                var secondTarget = WriteBranch(trueBlockFirst: isOr);
                WriteFloat(isOr ? 1f : 0f);

                var jump = WriteJump();
                WriteOffset(secondTarget);
                ParseBinary(precedence + 1);
                WriteOffset(jump);
            }
        }

        private bool TryConsumeOperator(int minPrecedence, out string symbol, out OpCode? opcode, out int precedence)
        {
            SkipWhitespace();

            foreach (var (operatorSymbol, op, operatorPrecedence) in BinaryOperators)
            {
                if (operatorPrecedence >= minPrecedence && source.AsSpan(position).StartsWith(operatorSymbol, StringComparison.Ordinal))
                {
                    position += operatorSymbol.Length;
                    symbol = operatorSymbol;
                    opcode = op;
                    precedence = operatorPrecedence;
                    return true;
                }
            }

            symbol = string.Empty;
            opcode = default;
            precedence = 0;
            return false;
        }

        private void ParseUnary()
        {
            if (TryConsume('-'))
            {
                ParseUnary();
                code.Add((byte)OpCode.Negate);
                return;
            }

            if (Peek() == '!' && PeekNext() != '=')
            {
                position++;
                ParseUnary();
                code.Add((byte)OpCode.Not);
                return;
            }

            ParsePrimary();

            while (Peek() == '.' && char.IsAsciiLetter(PeekNext()))
            {
                position++;
                ParseSwizzle();
            }
        }

        private void ParseSwizzle()
        {
            var start = position;
            var swizzle = 0;
            var component = 0;

            for (var i = 0; i < 4; i++)
            {
                if (position < source.Length && char.IsAsciiLetter(source[position]))
                {
                    component = source[position++] switch
                    {
                        'x' or 'r' => 0,
                        'y' or 'g' => 1,
                        'z' or 'b' => 2,
                        'w' or 'a' => 3,
                        _ => throw Error("Expected a swizzle"),
                    };
                }

                // Shorter swizzles repeat their last component
                swizzle |= component << (i * 2);
            }

            if (position < source.Length && char.IsAsciiLetterOrDigit(source[position]))
            {
                position = start;
                throw Error("Expected a swizzle of at most four components");
            }

            code.Add((byte)OpCode.Swizzle);
            code.Add((byte)swizzle);
        }

        private void ParsePrimary()
        {
            var next = Peek();

            if (TryConsume('('))
            {
                ParseExpression();
                Expect(')');
            }
            else if (char.IsAsciiDigit(next) || next == '.')
            {
                ParseNumber();
            }
            else if (char.IsAsciiLetter(next) || next == '_')
            {
                ParseName();
            }
            else
            {
                throw Error("Expected an expression");
            }
        }

        private void ParseNumber()
        {
            var start = position++;

            while (position < source.Length && (char.IsAsciiDigit(source[position]) || source[position] == '.'))
            {
                position++;
            }

            if (position < source.Length && source[position] is 'e' or 'E')
            {
                position++;

                if (position < source.Length && source[position] is '+' or '-')
                {
                    position++;
                }

                while (position < source.Length && char.IsAsciiDigit(source[position]))
                {
                    position++;
                }
            }

            if (!float.TryParse(source.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                position = start;
                throw Error("Expected a number");
            }

            WriteFloat(value);
        }

        private string? TryParseIdentifier()
        {
            var next = Peek();

            if (!char.IsAsciiLetter(next) && next != '_')
            {
                return null;
            }

            var start = position;

            while (position < source.Length && (char.IsAsciiLetterOrDigit(source[position]) || source[position] == '_'))
            {
                position++;
            }

            return source[start..position];
        }

        private void ParseName()
        {
            var name = TryParseIdentifier()!;

            if (locals.TryGetValue(name, out var slot))
            {
                code.Add((byte)OpCode.Load);
                code.Add(slot);
                return;
            }

            if (!TryConsume('('))
            {
                code.Add((byte)OpCode.MaterialParam);
                Write(StringToken.Store(name));
                return;
            }

            if (functions.TryGetValue(name, out var userFunction))
            {
                ExpandFunction(name, userFunction);
                return;
            }

            var function = FindFunction(name);

            if (function < 0)
            {
                throw Error($"Unknown function '{name}'");
            }

            var argumentCount = ParseArguments();

            if (argumentCount != VfxEval.Functions[function].ArgumentCount)
            {
                throw Error(string.Create(CultureInfo.InvariantCulture, $"'{name}' takes {VfxEval.Functions[function].ArgumentCount} arguments, not {argumentCount}"));
            }

            code.Add((byte)OpCode.Function);
            Write((ushort)function);
        }

        private int ParseArguments()
        {
            var argumentCount = 0;

            if (!TryConsume(')'))
            {
                do
                {
                    ParseExpression();
                    argumentCount++;
                }
                while (TryConsume(','));

                Expect(')');
            }

            return argumentCount;
        }

        // Stores the arguments in fresh locals, then compiles the body with only those in scope
        private void ExpandFunction(string name, UserFunction function)
        {
            var argumentCount = ParseArguments();

            if (argumentCount != function.Parameters.Length)
            {
                throw Error(string.Create(CultureInfo.InvariantCulture, $"'{name}' takes {function.Parameters.Length} arguments, not {argumentCount}"));
            }

            if (!expanding.Add(name))
            {
                throw Error($"'{name}' calls itself");
            }

            var scope = new Dictionary<string, byte>(function.Parameters.Length);

            for (var i = function.Parameters.Length - 1; i >= 0; i--)
            {
                var slot = AllocateSlot();
                scope[function.Parameters[i]] = slot;
                code.Add((byte)OpCode.Store);
                code.Add(slot);
            }

            var (callerLocals, callerPosition) = (locals, position);
            (locals, position) = (scope, function.Body);

            ParseStatements();
            Expect('}');

            (locals, position) = (callerLocals, callerPosition);
            expanding.Remove(name);
        }

        private byte AllocateSlot() => nextSlot <= byte.MaxValue ? (byte)nextSlot++ : throw Error("Too many locals");

        private void Write(ushort value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            code.AddRange(bytes);
        }

        private void Write(uint value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            code.AddRange(bytes);
        }

        private static int FindFunction(string name)
        {
            for (var i = 0; i < VfxEval.Functions.Count; i++)
            {
                if (VfxEval.Functions[i].Name == name)
                {
                    return i;
                }
            }

            return -1;
        }

        private char Peek()
        {
            SkipWhitespace();
            return position < source.Length ? source[position] : '\0';
        }

        private void SkipWhitespace()
        {
            while (position < source.Length && char.IsWhiteSpace(source[position]))
            {
                position++;
            }
        }

        private char PeekNext() => position + 1 < source.Length ? source[position + 1] : '\0';

        private bool TryConsume(char expected)
        {
            if (Peek() != expected)
            {
                return false;
            }

            position++;
            return true;
        }

        private void Expect(char expected)
        {
            if (!TryConsume(expected))
            {
                throw Error($"Expected '{expected}'");
            }
        }

        private InvalidDataException Error(string message) => new(string.Create(CultureInfo.InvariantCulture, $"{message} at position {position} of expression: {source}"));
    }
}
