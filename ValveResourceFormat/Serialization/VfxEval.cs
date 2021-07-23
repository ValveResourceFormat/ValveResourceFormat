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
        };

        private static readonly string[] OperatorSymbols = {
            "","","","","","","","","","","","","",
            "==","!=",">",">=","<","<=","+","-","*","/","%"};

        private enum OPCODE
        {
            ENDOFDATA = 0x00,
            BRANCH_SEP = 0x02,
            BRANCH = 0x04,
            FUNC = 0x06,
            FLOAT = 0x07,
            ASSIGN = 0x08,
            LOCALVAR = 0x09,
            NOT = 0x0C,
            EQUALS = 0x0D,               // 0D		==					13
            NEQUALS = 0x0E,              // 0E		!=					14
            GT = 0x0F,                   // 0F		> 					15
            GTE = 0x10,                  // 10		>=					16
            LT = 0x11,                   // 11		< 					17
            LTE = 0x12,                  // 12		<=					18
            ADD = 0x13,                  // 13		+					19
            SUB = 0x14,                  // 14		-					20
            MUL = 0x15,                  // 15		*					21
            DIV = 0x16,                  // 16		/					22
            MODULO = 0x17,               // 17		%					23
            NEGATE = 0x18,
            EXTVAR = 0x19,
            SWIZZLE = 0x1E,
            EXISTS = 0x1F,
            // NOT_AN_OPS = 0xff,
        };

        private readonly Stack<string> Expressions = new();

        // check on each OPS if we are exiting a branch,
        // when we do we should combine expressions on the stack
        private readonly Stack<uint> OffsetAtBranchExits = new();

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
            if (ExternalVarsReference.Count == 0)
            {
                BuildExternalVarsReference();
            }

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
                catch (System.ArgumentOutOfRangeException)
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
                            ErrorMessage = "error! - not on a branch exit";
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

                if (nrArguments == -1)
                {
                    ErrorWhileParsing = true;
                    ErrorMessage = $"Parsing error - unknown function ID = {funcId:x}";
                    return;
                }
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
                while (finalExp.Length > 2 && finalExp[0] == '(' && finalExp[finalExp.Length - 1] == ')')
                {
                    finalExp = Trimb(finalExp);
                }
                DynamicExpressionList.Add($"return {finalExp};");
                return;
            }

            // this point should never be reached
            // throw new Exception($"UNKNOWN OPCODE = 0x{(int)op:x2}, offset = {dataReader.BaseStream.Position}");
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
            string exp1 = Expressions.Pop();
            if (nrArguments == 1)
            {
                Expressions.Push($"{funcName}({Trimb(exp1)})");
                return;
            }
            string exp2 = Expressions.Pop();
            if (nrArguments == 2)
            {
                Expressions.Push($"{funcName}({Trimb(exp2)},{Trimb(exp1)})");
                return;
            }
            string exp3 = Expressions.Pop();
            if (nrArguments == 3)
            {
                // trim or not to trim ...
                Expressions.Push($"{funcName}({Trimb(exp3)},{Trimb(exp2)},{Trimb(exp1)})");
                // expressions.Push($"{funcName}({exp3},{exp2},{exp1})");
                return;
            }
            string exp4 = Expressions.Pop();
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
            return swizzle.Substring(0, i + 1);
        }

        private static string Trimb(string exp)
        {
            return exp[0] == '(' && exp[^1] == ')' ? exp[1..^1] : exp;
        }
        //private string Trimb2(string exp)
        //{
        //    return OffsetAtBranchExits.Count == 1 ? Trimb(exp) : exp;
        //}


        private readonly Dictionary<uint, string> ExternalVariablesPlaceholderNames = new();
        private readonly Dictionary<uint, string> LocalVariableNames = new();

        // naming external variables EXT, EXT2, EXT3,.. where not found
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
                    varName = "EXT";
                }
                else
                {
                    varName = string.Format("EXT{0}", ExternalVariablesPlaceholderNames.Count + 1);
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

        private static readonly Dictionary<uint, string> ExternalVarsReference = new();
        public static void BuildExternalVarsReference()
        {
            // Dota 2
            ExternalVarsReference.Add(0xd24b982f, "uiTexture");
            ExternalVarsReference.Add(0x8c954a0d, "fontTexture");
            ExternalVarsReference.Add(0xbaf50224, "scale1");
            ExternalVarsReference.Add(0x3b48bcd3, "scale2");
            ExternalVarsReference.Add(0x7dd532ad, "speed");
            ExternalVarsReference.Add(0x1ecf71e1, "a");
            ExternalVarsReference.Add(0xb7fdb72a, "b");
            ExternalVarsReference.Add(0x1df1849c, "intensity");
            ExternalVarsReference.Add(0x964485cc, "time");
            ExternalVarsReference.Add(0x336a0f0c, "$AGE");
            ExternalVarsReference.Add(0x1527c91c, "$ALPHA");
            ExternalVarsReference.Add(0xd772913d, "$TRANS_OFFSET_V");
            ExternalVarsReference.Add(0xa37a3e54, "$TRANS_SCALE_V");
            ExternalVarsReference.Add(0x25339664, "$OPACITY");
            ExternalVarsReference.Add(0x69e2f05e, "$TEX_COORD_OFFSET_U");
            ExternalVarsReference.Add(0x0a5b7f24, "$TEX_COORD_OFFSET_V");
            ExternalVarsReference.Add(0x7716a69a, "$PA_ARCANA_SPECULAR_BLOOM_SCALE");
            ExternalVarsReference.Add(0xd73c9c2f, "$PA_ARCANA_DETAIL1BLENDFACTOR");
            ExternalVarsReference.Add(0x287263fc, "$PA_ARCANA_DETAIL1SCALE");
            ExternalVarsReference.Add(0xd4147a1f, "$PA_ARCANA_DETAIL1TINT");
            ExternalVarsReference.Add(0xa58452dc, "$PA_ARCANA_SPECULAR_BLOOM_COLOR");
            ExternalVarsReference.Add(0x514616e6, "$GemColor");
            ExternalVarsReference.Add(0x9eac976a, "$overbright");
            ExternalVarsReference.Add(0xab2163a4, "$TEX_COLOR");
            ExternalVarsReference.Add(0x3225af29, "y");
            ExternalVarsReference.Add(0x84321e5f, "$DETAILBLEND");
            ExternalVarsReference.Add(0x276085fb, "FadeOut");
            ExternalVarsReference.Add(0xc4a1f8f7, "$COLOR");
            ExternalVarsReference.Add(0xf588a3d3, "$CLOAKINT");
            ExternalVarsReference.Add(0xda4e0212, "$SPIN");
            ExternalVarsReference.Add(0xb57746a1, "panoramaTexCoordOffset");
            ExternalVarsReference.Add(0xe244f4af, "panoramaTexCoordScale");
            ExternalVarsReference.Add(0x341f4361, "panoramaLayer");
            ExternalVarsReference.Add(0x1b927481, "avatarTexture");
            ExternalVarsReference.Add(0x57b2b714, "$ent_Health");
            ExternalVarsReference.Add(0x546a87df, "proceduralSprayTexture");
            ExternalVarsReference.Add(0x9d389d79, "alive");
            ExternalVarsReference.Add(0x46ec689a, "zz");
            ExternalVarsReference.Add(0x7068cf59, "aa");
            ExternalVarsReference.Add(0xff492a3a, "bb");
            ExternalVarsReference.Add(0xcb9a78d4, "cc");
            ExternalVarsReference.Add(0xde117c4a, "dd");
            ExternalVarsReference.Add(0xeb075669, "ee");
            ExternalVarsReference.Add(0x285fc55e, "ff");
            ExternalVarsReference.Add(0x39de2fbd, "FadeOut_blade");

            // HL Alyx
            ExternalVarsReference.Add(0xe7dc4bd6, "$BaseTexture");
            ExternalVarsReference.Add(0x30ee22ba, "colorAttrMovie");
            ExternalVarsReference.Add(0x98a42c96, "$DISSOLVE");
            ExternalVarsReference.Add(0x13e5d925, "$BRIGHTNESS");
            ExternalVarsReference.Add(0x097ad797, "logo_draw");
            ExternalVarsReference.Add(0xbad34216, "$SELFILLUM");
            ExternalVarsReference.Add(0xf8d95bff, "$SCROLLX");
            ExternalVarsReference.Add(0xfd912b88, "$SCROLLY");
            ExternalVarsReference.Add(0x0b2a3d85, "$EMISSIVEBRIGHTNESS");
            ExternalVarsReference.Add(0x41b948dc, "$EMISSIVESCALE");
            ExternalVarsReference.Add(0x8f3e65c3, "$NOISE");
            ExternalVarsReference.Add(0xd4db18d9, "$Emissive");
            ExternalVarsReference.Add(0x2797e0f8, "$Time");
            ExternalVarsReference.Add(0xa359c3d2, "$Enabled");
            ExternalVarsReference.Add(0x0a7ef0bc, "colorAttr");
            ExternalVarsReference.Add(0x52d9cda7, "$SPEED");
            ExternalVarsReference.Add(0x26b36985, "$FRESNEL");
            ExternalVarsReference.Add(0xd7d9c882, "$LINEWIDTH");
            ExternalVarsReference.Add(0x09e85963, "$COLOR2");
            ExternalVarsReference.Add(0xb4f6068c, "$EMISSIVE_COLOR");
            ExternalVarsReference.Add(0x9c865576, "$EyeBrightness");
            ExternalVarsReference.Add(0x626b58e4, "$TRANS");
            ExternalVarsReference.Add(0x99ed5df3, "gmanEyeGlow");
            ExternalVarsReference.Add(0xcba2f3ed, "$jawOpen");
            ExternalVarsReference.Add(0xe1ea5a51, "$ILLUMDEATH");
            ExternalVarsReference.Add(0xac2455ce, "$EmSpeed");
            ExternalVarsReference.Add(0x354cd34e, "useglow");
            ExternalVarsReference.Add(0xc2a33a98, "$IconCoordOffset");
            ExternalVarsReference.Add(0x64001e52, "$IconCoordScale");
            ExternalVarsReference.Add(0xfb2f9805, "$CounterIcon");
            ExternalVarsReference.Add(0x256a1960, "$CounterDigitHundreds");
            ExternalVarsReference.Add(0x4373b9f9, "$CounterDigitTens");
            ExternalVarsReference.Add(0x90c26f54, "$CounterDigitOnes");
            ExternalVarsReference.Add(0xbaeebc0b, "$HealthLights");
            ExternalVarsReference.Add(0xdea79565, "$FrameNumber1");
            ExternalVarsReference.Add(0x66a9e338, "$FrameNumber2");
            ExternalVarsReference.Add(0xcd41b4b8, "$FrameNumber3");
            ExternalVarsReference.Add(0xe4200216, "origin");
            ExternalVarsReference.Add(0x9550cca8, "value1");
            ExternalVarsReference.Add(0x7f787303, "$POSITION");
            ExternalVarsReference.Add(0xcb9c152d, "advisorMovie");
            ExternalVarsReference.Add(0xa55cfdd3, "$ANIM");
            ExternalVarsReference.Add(0xfac4270a, "$LightValue");
            ExternalVarsReference.Add(0xb5e34aab, "$PercentAwake");
            ExternalVarsReference.Add(0x435a062f, "$ENERGY");
            ExternalVarsReference.Add(0xe813cc7e, "$FLOW");
            ExternalVarsReference.Add(0x38b70d43, "$SCALE");
            ExternalVarsReference.Add(0x5a8f66c4, "$COLORA");
            ExternalVarsReference.Add(0x0f09ee7b, "$AnimatePipes");
            ExternalVarsReference.Add(0xbf319cc2, "$IlluminatePipes");
            ExternalVarsReference.Add(0x8715f68f, "$PistolChamberReadout");
            ExternalVarsReference.Add(0x98e238a9, "$PistolClipReadoutOffset");
            ExternalVarsReference.Add(0x90259463, "$PistolClipReadoutScale");
            ExternalVarsReference.Add(0xa58adf84, "$PistolHopperReadout");
            ExternalVarsReference.Add(0xc803a08e, "$InjectedPercent");
            ExternalVarsReference.Add(0x3666c43e, "$FrameNumber");
            ExternalVarsReference.Add(0x65f09c96, "$GrenadeLEDBrightness");
            ExternalVarsReference.Add(0x84e355f4, "$GrenadeLEDFuse");
            ExternalVarsReference.Add(0xc858079b, "$CableBrightness");
            ExternalVarsReference.Add(0x507c31f7, "$ChamberBrightness");
            ExternalVarsReference.Add(0x4f60e501, "$EnergyBallCharged");
            ExternalVarsReference.Add(0x47940a69, "$ReadyToExplode");
            ExternalVarsReference.Add(0x3cf1f4a5, "$AmmoColor");
            ExternalVarsReference.Add(0xefe71421, "$BulletCount");
            ExternalVarsReference.Add(0x79530848, "$MaxBulletCount");
            ExternalVarsReference.Add(0x71ee8c47, "$SlideLight");
            ExternalVarsReference.Add(0xe399c3c7, "$ShotgunHandleLight");
            ExternalVarsReference.Add(0x5260e007, "$QuickFireLight");
            ExternalVarsReference.Add(0x72711be3, "$LaserEmitterBrightness");
            ExternalVarsReference.Add(0x386b35f0, "$LaserEmitterFlowSpeed");
            ExternalVarsReference.Add(0x73119842, "$LightColor");
        }
    }
}
