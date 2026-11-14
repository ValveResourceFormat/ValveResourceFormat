using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using OPCODE = ValveResourceFormat.Serialization.VfxEval.VfxEval.OPCODE;

namespace ValveResourceFormat.Serialization.VfxEval
{
    /// <summary>
    /// Compiles expression text made of function calls, names, numbers and <c>?:</c> for <see cref="VfxEvalRuntime"/>.
    /// </summary>
    public sealed class VfxEvalCompiler
    {
        private readonly string source;
        private readonly List<byte> code = [];
        private int position;

        private VfxEvalCompiler(string source)
        {
            this.source = source;
        }

        /// <summary>Compiles an expression, throwing <see cref="InvalidDataException"/> when it is not valid.</summary>
        public static byte[] Compile(string source)
        {
            var compiler = new VfxEvalCompiler(source);
            compiler.ParseExpression();

            if (compiler.Peek() != '\0')
            {
                throw compiler.Error($"Unexpected '{compiler.Peek()}'");
            }

            compiler.code.Add((byte)OPCODE.RETURN);
            return [.. compiler.code];
        }

        private void ParseExpression()
        {
            ParsePrimary();

            if (!TryConsume('?'))
            {
                return;
            }

            code.Add((byte)OPCODE.BRANCH);
            var branch = code.Count;
            code.AddRange(new byte[4]);
            WriteOffset(branch);

            ParseExpression();
            Expect(':');

            code.Add((byte)OPCODE.JUMP);
            var jump = code.Count;
            code.AddRange(new byte[2]);
            WriteOffset(branch + 2);

            ParseExpression();
            WriteOffset(jump);
        }

        // Points a placeholder at the next instruction
        private void WriteOffset(int at) => BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(code)[at..], (ushort)code.Count);

        private void ParsePrimary()
        {
            var next = Peek();

            if (TryConsume('('))
            {
                ParseExpression();
                Expect(')');
            }
            else if (char.IsAsciiDigit(next) || next is '.' or '-')
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

            if (!float.TryParse(source.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                position = start;
                throw Error("Expected a number");
            }

            code.Add((byte)OPCODE.FLOAT);
            Write(BitConverter.SingleToUInt32Bits(value));
        }

        private void ParseName()
        {
            var start = position;

            while (position < source.Length && (char.IsAsciiLetterOrDigit(source[position]) || source[position] == '_'))
            {
                position++;
            }

            var name = source[start..position];

            if (!TryConsume('('))
            {
                code.Add((byte)OPCODE.MATERIAL_PARAM);
                Write(StringToken.Store(name));
                return;
            }

            var function = Array.FindIndex(VfxEval.FUNCTION_REF, f => f.Name == name);

            if (function < 0)
            {
                throw Error($"Unknown function '{name}'");
            }

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

            if (argumentCount != VfxEval.FUNCTION_REF[function].ArgumentCount)
            {
                throw Error(string.Create(CultureInfo.InvariantCulture, $"'{name}' takes {VfxEval.FUNCTION_REF[function].ArgumentCount} arguments, not {argumentCount}"));
            }

            code.Add((byte)OPCODE.FUNC);
            Write((ushort)function);
        }

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

        private char Peek()
        {
            while (position < source.Length && char.IsWhiteSpace(source[position]))
            {
                position++;
            }

            return position < source.Length ? source[position] : '\0';
        }

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
