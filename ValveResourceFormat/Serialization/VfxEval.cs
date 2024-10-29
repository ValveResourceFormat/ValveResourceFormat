using System.Collections.Concurrent;
using System.IO;
using System.Text;
using ValveResourceFormat.ThirdParty;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Serialization.VfxEval
{
    public class VfxEval
    {
        // parsed data assigned here
        public string DynamicExpressionResult { get; private set; }
        // parse the input one line at a time
        private readonly List<string> DynamicExpressionList = [];

        // function reference, name and number of arguments
        private readonly (string Name, int ArgumentCount)[] FUNCTION_REF = [
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
            ("TextureSize",1),     // 24
            ("rotation2d", 1),     // 25
            ("rotate2d",   2),     // 26
            ("sincos",     1),     // 27
            ("TextureSize",1),     // 28
            ("TextureAverageColor", 1), // 29
            ("MatrixIdentity",      0), // 2A
            ("MatrixScale",         1), // 2B
            ("MatrixTranslate",     1), // 2C
            ("MatrixAxisAngle",     1), // 2D
            ("MatrixAxisToAxis",    2), // 2E
            ("MatrixMultiply",      2), // 2F
            ("MatrixColorCorrect",  1), // 30
            ("MatrixColorCorrect2", 2), // 31
            ("MatrixColorTint",     1), // 32
            ("normalize_safe",      1), // 33
            ("Remap01ScaleOffset",  1), // 34
            ("radians",             1), // 35
            ("degrees",             1), // 36
            ("MatrixColorTint2",    2), // 37
            ("MatrixColorTint3",    3), // 38
            ("RemapVal",            5), // 39
            ("RemapValClamped",     5), // 3A
#pragma warning restore format
        ];

        private enum OPCODE
        {
            ENDOFDATA,          // 00
            UNKNOWN01,
            BRANCH_SEP,         // 02
            UNKNOWN03,
            BRANCH,             // 04
            UNKNOWN05,
            FUNC,               // 06
            FLOAT,              // 07
            ASSIGN,             // 08
            LOCALVAR,           // 09
            UNKNOWN0A,
            UNKNOWN0B,
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
            EXTVAR,             // 19
            COND,               // 1A (inferred from the shader code)
            UNKNOWN1B,
            UNKNOWN1C,
            EVAL,               // 1D (inferred from the shader code)
            SWIZZLE,            // 1E
            EXISTS,             // 1F
            UNKNOWN20,
            UNKNOWN21,
        };

        private static readonly Dictionary<OPCODE, string> OpCodeToSymbol = new()
        {
            { OPCODE.EQUALS, "==" },
            { OPCODE.NEQUALS, "!=" },
            { OPCODE.GT, ">" },
            { OPCODE.GTE, ">=" },
            { OPCODE.LT, "<" },
            { OPCODE.LTE, "<=" },
            { OPCODE.ADD, "+" },
            { OPCODE.SUB, "-" },
            { OPCODE.MUL, "*" },
            { OPCODE.DIV, "/" },
            { OPCODE.MODULO, "%" },
        };

        private const uint IFELSE_BRANCH = 0;     //    <cond> : <e1> ? <e2>
        private const uint AND_BRANCH = 1;        //    <e1> && <e2>            (these expressions are encoded as branches on the bytestream!)
        private const uint OR_BRANCH = 2;         //    <e1> || <e2>

        private readonly Stack<string> Expressions = new();

        // check on each OPS if we are exiting a branch,
        // when we do we should combine expressions on the stack
        private readonly Stack<uint> OffsetAtBranchExits = new();
        private readonly Dictionary<uint, string> LocalVariableNames = [];

        // The 'return' keyword in the last line of a dynamic expression is optional (it is implied where absent)
        // OmitReturnStatement controls whether it is shown
        private readonly bool OmitReturnStatement;

        private readonly IReadOnlyList<string> Features;

        public VfxEval(byte[] binaryBlob, bool omitReturnStatement = false, IReadOnlyList<string> features = null)
        {
            OmitReturnStatement = omitReturnStatement;
            Features = features;
            ParseExpression(binaryBlob);
        }

        public VfxEval(byte[] binaryBlob, IReadOnlyList<string> renderAttributesUsed, bool omitReturnStatement = false, IReadOnlyList<string> features = null)
        {
            OmitReturnStatement = omitReturnStatement;
            Features = features;

            foreach (var renderAttribute in renderAttributesUsed)
            {
                StringToken.Get(renderAttribute);
            }

            ParseExpression(binaryBlob);
        }

        private void ParseExpression(byte[] binaryBlob)
        {
            using var dataReader = new BinaryReader(new MemoryStream(binaryBlob));

            while (dataReader.BaseStream.Position < binaryBlob.Length)
            {
                ProcessOps((OPCODE)dataReader.ReadByte(), dataReader);
            }

            foreach (var expression in DynamicExpressionList)
            {
                DynamicExpressionResult += $"{expression}\n";
            }

            DynamicExpressionResult = DynamicExpressionResult.Trim();
        }

        private void ProcessOps(OPCODE op, BinaryReader dataReader)
        {
            // when exiting a branch, combine the conditional expressions on the stack into one
            while (OffsetAtBranchExits.Count > 0
                && OffsetAtBranchExits.Peek() == dataReader.BaseStream.Position)
            {
                OffsetAtBranchExits.Pop();
                var branchType = OffsetAtBranchExits.Pop();

                switch (branchType)
                {
                    case IFELSE_BRANCH:
                        if (Expressions.Count < 3)
                        {
                            throw new InvalidDataException($"Error parsing dynamic expression, insufficient expressions evaluating IFELSE_BRANCH (position: {dataReader.BaseStream.Position})");
                        }
                        {
                            var exp3 = Expressions.Pop();
                            var exp2 = Expressions.Pop();
                            var exp1 = Expressions.Pop();
                            // it's not safe to trim here
                            // string expConditional = $"({Trimbrackets(exp1)} ? {Trimbrackets(exp2)} : {Trimbrackets(exp3)})";
                            var expConditional = $"({exp1} ? {exp2} : {exp3})";
                            Expressions.Push(expConditional);
                        }
                        break;

                    case AND_BRANCH:
                        if (Expressions.Count < 2)
                        {
                            throw new InvalidDataException($"Error parsing dynamic expression, insufficient expressions evaluating AND_BRANCH (position: {dataReader.BaseStream.Position})");
                        }
                        {
                            var exp2 = Expressions.Pop();
                            var exp1 = Expressions.Pop();
                            var expAndConditional = $"({exp1} && {exp2})";
                            Expressions.Push(expAndConditional);
                        }
                        break;

                    case OR_BRANCH:
                        if (Expressions.Count < 2)
                        {
                            throw new InvalidDataException($"Error parsing dynamic expression, insufficient expressions evaluating OR_BRANCH (position: {dataReader.BaseStream.Position})");
                        }
                        {
                            var exp2 = Expressions.Pop();
                            var exp1 = Expressions.Pop();
                            var expOrConditional = $"({exp1} || {exp2})";
                            Expressions.Push(expOrConditional);
                        }
                        break;

                    default:
                        throw new InvalidDataException($"Error parsing dynamic expression, unknown branch switch ({branchType}) (position: {dataReader.BaseStream.Position})");
                }
            }

            if (op == OPCODE.BRANCH_SEP)
            {
                var branchExit = (uint)dataReader.ReadUInt16();
                OffsetAtBranchExits.Push(branchExit + 1);
                return;
            }

            // we will need the branch exit, it becomes available when we get to the branch separator
            // (in the middle of the conditional structure)
            if (op == OPCODE.BRANCH)
            {
                var pointer1 = dataReader.ReadUInt16();
                var pointer2 = dataReader.ReadUInt16();
                var b = dataReader.ReadBytes(5);

                // for <e1>&&<e2> expressions we are looking for the pattern
                // 04 12 00 0A 00    07 00 00 00 00
                if (pointer1 - pointer2 == 8 && b[0] == 7 && b[1] == 0 && b[2] == 0 && b[3] == 0 && b[4] == 0)
                {
                    OffsetAtBranchExits.Push(AND_BRANCH);
                    return;
                }

                // for <e1>||<e2> expressions we are looking for the pattern
                // 04 17 00 1F 00     07 00 00 80 3F
                if (pointer2 - pointer1 == 8 && b[0] == 7 && b[1] == 0 && b[2] == 0 && b[3] == 0x80 && b[4] == 0x3F)
                {
                    OffsetAtBranchExits.Push(OR_BRANCH);
                    return;
                }

                // rewind the 5 bytes read above
                dataReader.BaseStream.Position -= 5;
                OffsetAtBranchExits.Push(IFELSE_BRANCH);
                return;
            }

            if (op == OPCODE.FUNC)
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

                if (nrArguments > Expressions.Count)
                {
                    throw new InvalidDataException($"Error parsing dynamic expression, insufficient expressions evaluatuating function {funcName} (position: {dataReader.BaseStream.Position})");
                }

                ApplyFunction(funcName, nrArguments);
                return;
            }

            if (op == OPCODE.FLOAT)
            {
                var floatVal = dataReader.ReadSingle();
                var floatLiteral = $"{floatVal:g}";
                // if a float leads with "0." remove the 0 (as how Valve likes it)
                if (floatLiteral.Length > 1 && floatLiteral[..2] == "0.")
                {
                    floatLiteral = floatLiteral[1..];
                }
                Expressions.Push(floatLiteral);
                return;
            }

            // assignment is always to a local variable, and it terminates the line
            if (op == OPCODE.ASSIGN)
            {
                var varId = dataReader.ReadByte();
                var locVarname = GetLocalVarName(varId);
                var exp = Expressions.Pop();
                var assignExpression = $"{locVarname} = {Trimbrackets(exp)};";
                DynamicExpressionList.Add(assignExpression);
                return;
            }

            if (op == OPCODE.LOCALVAR)
            {
                var varId = dataReader.ReadByte();
                var locVarname = GetLocalVarName(varId);
                Expressions.Push(locVarname);
                return;
            }

            if (op == OPCODE.NOT)
            {
                var exp = Expressions.Pop();
                Expressions.Push($"!{exp}");
                return;
            }

            if (op >= OPCODE.EQUALS && op <= OPCODE.MODULO)
            {
                if (Expressions.Count < 2)
                {
                    throw new InvalidDataException($"Error parsing dynamic expression, insufficient expressions for operation {op} (position: {dataReader.BaseStream.Position})");
                }
                var exp2 = Expressions.Pop();
                var exp1 = Expressions.Pop();
                Expressions.Push($"({exp1}{OpCodeToSymbol[op]}{exp2})");
                return;
            }

            if (op == OPCODE.NEGATE)
            {
                var exp = Expressions.Pop();
                Expressions.Push($"-{exp}");
                return;
            }

            if (op == OPCODE.EXTVAR)
            {
                var varId = dataReader.ReadUInt32();
                var extVarname = GetExternalVarName(varId);
                Expressions.Push(extVarname);
                return;
            }

            if (op == OPCODE.COND)
            {
                uint expressionId = dataReader.ReadByte();
                Expressions.Push((Features is null) ? $"COND[{expressionId}]" : Features[(int)expressionId]);
                return;
            }

            if (op == OPCODE.EVAL)
            {
                var intval = dataReader.ReadUInt32();
                // if this reference exists in the vars-reference, then show it
                if (StringToken.InvertedTable.TryGetValue(intval, out var externalVar))
                {
                    Expressions.Push(externalVar);
                }
                else
                {
                    Expressions.Push($"EVAL[{intval:x08}]");
                }
                return;
            }

            if (op == OPCODE.SWIZZLE)
            {
                var exp = Expressions.Pop();
                var swizzle = GetSwizzle(dataReader.ReadByte());
                Expressions.Push($"{exp}.{swizzle}");
                return;
            }

            if (op == OPCODE.EXISTS)
            {
                var varId = dataReader.ReadUInt32();
                var extVarname = GetExternalVarName(varId);
                Expressions.Push($"exists({extVarname})");
                return;
            }

            // parser terminates here
            if (op == OPCODE.ENDOFDATA)
            {
                if (dataReader.PeekChar() != -1)
                {
                    throw new InvalidDataException($"Looks like we did not read the data correctly (position: {dataReader.BaseStream.Position})");
                }
                var finalExp = Expressions.Pop();
                finalExp = Trimbrackets(finalExp);
                if (OmitReturnStatement)
                {
                    DynamicExpressionList.Add(finalExp);
                }
                else
                {
                    DynamicExpressionList.Add($"return {finalExp};");
                }
                return;
            }

            throw new InvalidDataException($"Error parsing dynamic expression, unknown opcode = 0x{(int)op:x2} (position: {dataReader.BaseStream.Position})");
        }

        private void ApplyFunction(string funcName, int nrArguments)
        {
            var arguments = new Stack<string>(nrArguments);
            for (var i = 0; i < nrArguments; i++)
            {
                arguments.Push(Expressions.Pop());
            }

            var expression = new StringBuilder();
            expression.Append(funcName);
            expression.Append('(');

            for (var i = 0; i < nrArguments; i++)
            {
                if (i > 0)
                {
                    expression.Append(',');
                }

                expression.Append(Trimbrackets(arguments.Pop()));
            }

            expression.Append(')');

            Expressions.Push(expression.ToString());
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

        // The decompiler has a tendency to accumulate brackets so we trim them in places where
        // it is safe (which is just done for readability).
        // The approach to removing brackets is not optimised in any way, arithmetic expressions
        // will accumulate brackets and it's not trivial to know when it's safe to remove them
        // For example 1+2+3+4 will decompile as ((1+2)+3)+4
        private static string Trimbrackets(string exp)
        {
            return exp[0] == '(' && exp[^1] == ')' ? exp[1..^1] : exp;
        }

        // if the variable reference is unknown return in the form UNKNOWN[e46d252d] (showing the murmur32)
        private static string GetExternalVarName(uint varId)
        {
            StringToken.InvertedTable.TryGetValue(varId, out var varKnownName);
            if (varKnownName != null)
            {
                return varKnownName;
            }
            else
            {
                return $"UNKNOWN[{varId:x08}]";
            }
        }

        // naming local variables v0,v1,v2,..
        private string GetLocalVarName(uint varId)
        {
            LocalVariableNames.TryGetValue(varId, out var varName);
            if (varName == null)
            {
                varName = $"v{LocalVariableNames.Count}";
                LocalVariableNames.Add(varId, varName);
            }
            return varName;
        }
    }
}
