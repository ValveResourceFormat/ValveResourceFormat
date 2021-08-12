using System;
using System.Collections.Generic;
using System.IO;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Serialization.VfxEval
{
    public class VfxEval
    {
        // parsed data assigned here
        public string DynamicExpressionResult { get; private set; }
        // parse the input one line at a time
        private readonly List<string> DynamicExpressionList = new();

        // check externally if we have problems parsing
        public bool ErrorWhileParsing { get; private set; }
        public string ErrorMessage { get; private set; }


        // function reference, name and number of arguments
        private readonly (string, int)[] FUNCTION_REF = {
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
        };

        private static readonly string[] OperatorSymbols = {
            "","","","","","","","","","","","","",
            "==","!=",">",">=","<","<=","+","-","*","/","%"};

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
            EQUALS,             // 0D (13)  ==
            NEQUALS,            // 0E (14)	!=
            GT,                 // 0F (15)	>
            GTE,                // 10 (16)	>=
            LT,                 // 11 (17)	<
            LTE,                // 12 (18)	<=
            ADD,                // 13 (19)	+
            SUB,                // 14 (20)	-
            MUL,                // 15 (21)	*
            DIV,                // 16 (22)	/
            MODULO,             // 17 (23)	%
            NEGATE,             // 18
            EXTVAR,             // 19
            UNKNOWN1A,
            UNKNOWN1B,
            UNKNOWN1C,
            UNKNOWN1D,
            SWIZZLE,            // 1E
            EXISTS,             // 1F
            UNKNOWN20,
            UNKNOWN21,
            // NOT_AN_OPS = 0xff,
        };

        private readonly Stack<string> Expressions = new();

        // check on each OPS if we are exiting a branch,
        // when we do we should combine expressions on the stack
        private readonly Stack<uint> OffsetAtBranchExits = new();

        // build a dictionary of the external variables seen, passed as 'renderAttributesUsed'
        private static readonly Dictionary<uint, string> ExternalVarsReference = new();

        public VfxEval(byte[] binaryBlob)
        {
            ParseExpression(binaryBlob, Array.Empty<string>());
        }
        public VfxEval(byte[] binaryBlob, string[] renderAttributesUsed)
        {
            ParseExpression(binaryBlob, renderAttributesUsed);
        }

        private void ParseExpression(byte[] binaryBlob, string[] renderAttributesUsed)
        {
            uint MURMUR2SEED = 0x31415926; // pi!
            foreach (var externalVarName in renderAttributesUsed)
            {
                var murmur32 = MurmurHash2.Hash(externalVarName.ToLower(), MURMUR2SEED);
                ExternalVarsReference.TryGetValue(murmur32, out var varName);
                // overwrite existing entries because newer additions may have different case
                if (varName != null)
                {
                    ExternalVarsReference.Remove(murmur32);
                }
                ExternalVarsReference.Add(murmur32, externalVarName);
            }

            using var dataReader = new BinaryReader(new MemoryStream(binaryBlob));
            OffsetAtBranchExits.Push(0);

            while (dataReader.BaseStream.Position < binaryBlob.Length)
            {
                try
                {
                    ProcessOps((OPCODE)dataReader.ReadByte(), dataReader);
                }
                catch (System.IO.EndOfStreamException)
                {
                    ErrorWhileParsing = true;
                    ErrorMessage = "Parsing error - reader exceeded input";
                }
                if (ErrorWhileParsing)
                {
                    return;
                }
            }
            foreach (var expression in DynamicExpressionList)
            {
                DynamicExpressionResult += $"{expression}\n";
            }
            DynamicExpressionResult = DynamicExpressionResult.Trim();
        }


        private const uint IFELSE_BRANCH = 0;     //    <cond> : <e1> ? <e2>
        private const uint AND_BRANCH = 1;        //    <e1> && <e2>            (these expressions are encoded as branches on the bytestream!)
        private const uint OR_BRANCH = 2;         //    <e1> || <e2>

        private void ProcessOps(OPCODE op, BinaryReader dataReader)
        {
            // when exiting a branch, combine the conditional expressions on the stack into one
            if (OffsetAtBranchExits.Peek() == dataReader.BaseStream.Position)
            {
                OffsetAtBranchExits.Pop();
                var branchType = OffsetAtBranchExits.Pop();

                switch (branchType)
                {
                    case IFELSE_BRANCH:
                        if (Expressions.Count < 3)
                        {
                            ErrorWhileParsing = true;
                            ErrorMessage = "parse error - not on a branch exit";
                            return;
                        }
                        {
                            var exp3 = Expressions.Pop();
                            var exp2 = Expressions.Pop();
                            var exp1 = Expressions.Pop();
                            // it's not safe to trim here
                            // string expConditional = $"({trimb2(exp1)} ? {trimb2(exp2)} : {trimb2(exp3)})";
                            var expConditional = $"({exp1} ? {exp2} : {exp3})";
                            Expressions.Push(expConditional);
                        }
                        break;

                    case AND_BRANCH:
                        if (Expressions.Count < 2)
                        {
                            ErrorWhileParsing = true;
                            ErrorMessage = "parse error, evaluating AND_BRANCH";
                            return;
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
                            ErrorWhileParsing = true;
                            ErrorMessage = "parse error, evaluating OR_BRANCH";
                            return;
                        }
                        {
                            var exp2 = Expressions.Pop();
                            var exp1 = Expressions.Pop();
                            var expOrConditional = $"({exp1} || {exp2})";
                            Expressions.Push(expOrConditional);
                        }
                        break;

                    default:
                        ErrorWhileParsing = true;
                        ErrorMessage = "error! this should not happen";
                        return;
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
                    ErrorWhileParsing = true;
                    ErrorMessage = $"Parsing error - invalid function Id = {funcId:x}";
                    return;
                }
                if (funcCheckByte != 0)
                {
                    ErrorWhileParsing = true;
                    ErrorMessage = $"Parsing error - malformed data";
                    return;
                }
                var funcName = FUNCTION_REF[funcId].Item1;
                var nrArguments = FUNCTION_REF[funcId].Item2;

                if (nrArguments > Expressions.Count)
                {
                    ErrorWhileParsing = true;
                    ErrorMessage = $"Parsing error - too many arguments!";
                    return;
                }

                ApplyFunction(funcName, nrArguments);
                return;
            }

            if (op == OPCODE.FLOAT)
            {
                var floatVal = dataReader.ReadSingle();
                var floatLiteral = string.Format("{0:g}", floatVal);
                // if a float leads with "0." remove the 0 (as how Valve likes it)
                if (floatLiteral.Length > 1 && floatLiteral.Substring(0, 2) == "0.")
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
                var assignExpression = $"{locVarname} = {Trimb(exp)};";
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
                    ErrorWhileParsing = true;
                    ErrorMessage = $"Parsing error - missing expressions, cannot build the operation {op}";
                    return;
                }
                var exp2 = Expressions.Pop();
                var exp1 = Expressions.Pop();
                var opSymbol = OperatorSymbols[(int)op];
                Expressions.Push($"({exp1}{opSymbol}{exp2})");
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

            if (op == OPCODE.SWIZZLE)
            {
                var exp = Expressions.Pop();
                exp += $".{GetSwizzle(dataReader.ReadByte())}";
                Expressions.Push($"{exp}");
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
                    ErrorMessage = "malformed data!";
                    ErrorWhileParsing = true;
                    return;
                }
                var finalExp = Expressions.Pop();
                finalExp = Trimb(finalExp);
                DynamicExpressionList.Add($"return {finalExp};");
                return;
            }

            // this point should never be reached
            ErrorWhileParsing = true;
            ErrorMessage = $"UNKNOWN OPCODE = 0x{(int)op:x2}, offset = {dataReader.BaseStream.Position}";
        }

        private void ApplyFunction(string funcName, int nrArguments)
        {
            if (nrArguments == 0)
            {
                Expressions.Push($"{funcName}()");
                return;
            }
            var exp1 = Expressions.Pop();
            if (nrArguments == 1)
            {
                Expressions.Push($"{funcName}({Trimb(exp1)})");
                return;
            }
            var exp2 = Expressions.Pop();
            if (nrArguments == 2)
            {
                Expressions.Push($"{funcName}({Trimb(exp2)},{Trimb(exp1)})");
                return;
            }
            var exp3 = Expressions.Pop();
            if (nrArguments == 3)
            {
                // trim or not to trim ...
                Expressions.Push($"{funcName}({Trimb(exp3)},{Trimb(exp2)},{Trimb(exp1)})");
                // expressions.Push($"{funcName}({exp3},{exp2},{exp1})");
                return;
            }
            var exp4 = Expressions.Pop();
            if (nrArguments == 4)
            {
                Expressions.Push($"{funcName}({Trimb(exp4)},{Trimb(exp3)},{Trimb(exp2)},{Trimb(exp1)})");
                return;
            }

            throw new Exception("this cannot happen!");
        }

        private static string GetSwizzle(byte b)
        {
            string[] axes = { "x", "y", "z", "w" };
            var swizzle = axes[b & 3] + axes[(b >> 2) & 3] + axes[(b >> 4) & 3] + axes[(b >> 6) & 3];
            var i = 3;
            while (i > 0 && swizzle[i - 1] == swizzle[i])
            {
                i--;
            }
            return swizzle[0..(i + 1)];
        }

        private static string Trimb(string exp)
        {
            return exp[0] == '(' && exp[^1] == ')' ? exp[1..^1] : exp;
        }


        private readonly Dictionary<uint, string> ExternalVariablesPlaceholderNames = new();
        private readonly Dictionary<uint, string> LocalVariableNames = new();

        // naming external variables UNKNOWN, UNKNOWN2, UNKNOWN3,.. where not found
        private string GetExternalVarName(uint varId)
        {
            ExternalVarsReference.TryGetValue(varId, out var varKnownName);
            if (varKnownName != null)
            {
                return varKnownName;
            }
            ExternalVariablesPlaceholderNames.TryGetValue(varId, out var varName);
            if (varName == null)
            {
                if (ExternalVariablesPlaceholderNames.Count == 0)
                {
                    varName = "UNKNOWN";
                }
                else
                {
                    varName = string.Format("UNKNOWN{0}", ExternalVariablesPlaceholderNames.Count + 1);
                }
                ExternalVariablesPlaceholderNames.Add(varId, varName);
            }
            return varName;
        }

        // naming local variables v1,v2,v3,..
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
