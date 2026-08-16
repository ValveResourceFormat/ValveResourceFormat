using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat.Serialization.VfxEval;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class VfxEvalTests
    {
        /*
         * random(1,2)
         */
        [Test]
        public async Task TestDynamicExpression1()
        {
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 06 20 00 00";
            var expectedResult = "return random(1,2);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestDynamicExpression2()
        {
            var exampleStr =
            "07 CD CC 4C 3F 07 00 00 80 3F 06 20 00 08 00 07 00 00 80 3F 07 00 00 00 40 06 20 00 08 01 07 00 " +
            "00 00 00 07 00 00 80 3F 06 20 00 08 02 09 02 07 CD CC CC 3D 0F 04 3A 00 3F 00 09 00 02 41 00 09 " +
            "01 08 03 07 00 00 80 3F 09 03 15 07 00 00 80 3F 09 03 15 07 00 00 80 3F 09 03 15 06 19 00 08 04 " +
            "09 04 07 00 00 80 3F 15 00";
            var expectedResult = "v0 = random(.8,1);\n" +
                "v1 = random(1,2);\n" +
                "v2 = random(0,1);\n" +
                "v3 = (v2>.1) ? v0 : v1;\n" +
                "v4 = float3(1*v3,1*v3,1*v3);\n" +
                "return v4*1;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestDynamicExpression3()
        {
            var exampleStr =
                "19 38 AE 48 52 19 31 FB FD 02 0F 08 00 " +
                "19 38 AE 48 52 19 31 FB FD 02 11 08 00 " +
                "19 38 AE 48 52 19 31 FB FD 02 0D 08 00 " +
                "19 38 AE 48 52 19 31 FB FD 02 10 08 00 " +
                "19 38 AE 48 52 19 31 FB FD 02 12 08 00 " +
                "19 38 AE 48 52 19 31 FB FD 02 0E 08 00 " +
                "09 00 06 03 00 00";
            var expectedResult =
                "v0 = ATTRIBUTE[5248ae38]>ATTRIBUTE[02fdfb31];\n" +
                "v0 = ATTRIBUTE[5248ae38]<ATTRIBUTE[02fdfb31];\n" +
                "v0 = ATTRIBUTE[5248ae38]==ATTRIBUTE[02fdfb31];\n" +
                "v0 = ATTRIBUTE[5248ae38]>=ATTRIBUTE[02fdfb31];\n" +
                "v0 = ATTRIBUTE[5248ae38]<=ATTRIBUTE[02fdfb31];\n" +
                "v0 = ATTRIBUTE[5248ae38]!=ATTRIBUTE[02fdfb31];\n" +
                "return frac(v0);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * true ? 2 : 3
         *
         * interpreted as
         *
         *  1 ? 2 : 3;
         *
         */
        [Test]
        public async Task TestDynamicExpression4()
        {
            var exampleStr = "07 00 00 80 3F 04 0A 00 12 00 07 00 00 00 40 02 17 00 07 00 00 40 40 00";
            var expectedResult = "return 1 ? 2 : 3;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         *      a = length(1);
         *      a = sqrt(1);
         *      a = rotation2d(1);
         *      frac(a)
         */
        [Test]
        public async Task TestDynamicExpression5()
        {
            var exampleStr = "07 00 00 80 3F 06 22 00 08 00 07 00 00 80 3F 06 11 00 08 00 07 00 00 80 3F 06 24 00 08 00 09 00 06 03 00 00";
            var expectedResult =
                "v0 = length(1);\n" +
                "v0 = sqrt(1);\n" +
                "v0 = rotation2d(1);\n" +
                "return frac(v0);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestDynamicExpression6()
        {
            var exampleStr = "07 00 00 80 3F 07 00 00 80 40 13 08 00 07 00 00 20 41 07 00 00 20 42 13 00";
            var expectedResult = "v0 = 1+4;\nreturn 10+40;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * true ? (true ? 1 : 2) : 123
         *
         * true will be replaced by 1
         *
         */
        [Test]
        public async Task TestDynamicExpression7()
        {
            var exampleStr = "07 00 00 80 3F 04 0A 00 24 00 07 00 00 80 3F 04 14 00 1C 00 07 00 00 80 3F " +
                "02 21 00 07 00 00 00 40 02 29 00 07 00 00 F6 42 00";
            var expectedResult = "return 1 ? (1 || 2) : 123;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * v1 && v2 ? frac(10) : 100*100
         *
         * the && is compiled as a branch whose constant-0 block is written first,
         * which is how it is told apart from a plain conditional
         */
        [Test]
        public async Task TestDynamicExpression8()
        {
            var exampleStr = "19 38 AE 48 52 04 12 00 0A 00 07 00 00 00 00 02 17 00 19 31 FB FD 02 04 1C 00 27 00 07 00 00 20 " +
               "41 06 03 00 02 32 00 07 00 00 C8 42 07 00 00 C8 42 15 00";
            var expectedResult = "return (ATTRIBUTE[5248ae38] && ATTRIBUTE[02fdfb31]) ? frac(10) : 100*100;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * a = 10+10;
         * v1||v2 ? sin(1) : 7
         *
         */
        [Test]
        public async Task TestDynamicExpression9()
        {
            var exampleStr = "07 00 00 20 41 07 00 00 20 41 13 08 00 19 38 AE 48 52 04 17 00 1F 00 07 00 00 80 3F 02 24 00 19 " +
                "31 FB FD 02 04 29 00 34 00 07 00 00 80 3F 06 00 00 02 39 00 07 00 00 E0 40 00";
            var expectedResult =
                "v0 = 10+10;\n" +
                "return (ATTRIBUTE[5248ae38] || ATTRIBUTE[02fdfb31]) ? sin(1) : 7;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         *    v0 = random(.8,1);
         *    v1 = random(1,2);
         *    v2 = random(0,1);
         *    v3 = (v2>.1) ? v0 : v1;
         *    v4 = float3(1*v3,1*v3,1*v3);
         *    return v4*1;
         *
         */
        [Test]
        public async Task TestDynamicExpression10()
        {
            var exampleStr =
            "07 CD CC 4C 3F 07 00 00 80 3F 06 20 00 08 00 07 00 00 80 3F 07 00 00 00 40 06 20 00 08 01 07 00 " +
            "00 00 00 07 00 00 80 3F 06 20 00 08 02 09 02 07 CD CC CC 3D 0F 04 3A 00 3F 00 09 00 02 41 00 09 " +
            "01 08 03 07 00 00 80 3F 09 03 15 07 00 00 80 3F 09 03 15 07 00 00 80 3F 09 03 15 06 19 00 08 04 " +
            "09 04 07 00 00 80 3F 15 00";
            var expectedResult =
                "v0 = random(.8,1);\n" +
                "v1 = random(1,2);\n" +
                "v2 = random(0,1);\n" +
                "v3 = (v2>.1) ? v0 : v1;\n" +
                "v4 = float3(1*v3,1*v3,1*v3);\n" +
                "return v4*1;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         *  v0 = sin(EXT);
         *  v1 = exists(EXT2) ? float4(1,2,3,4) : float4(5,6,7,8);
         *  v2 = cos(v0);
         *  return v0+(dot4(v1,EXT3.xyz)*v2);
         *
         */
        [Test]
        public async Task TestDynamicExpression11()
        {
            var exampleStr =
            "19 D6 AA E4 2C 06 00 00 08 00 1F 39 F1 28 39 04 14 00 2E 00 07 00 00 80 3F 07 00 00 00 40 07 00 " +
            "00 40 40 07 00 00 80 40 06 18 00 02 45 00 07 00 00 A0 40 07 00 00 C0 40 07 00 00 E0 40 07 00 00 " +
            "00 41 06 18 00 08 01 09 00 06 01 00 08 02 09 00 09 01 19 15 D1 7D 0F 1E A4 06 09 00 09 02 15 13 00";
            var expectedResult =
                "v0 = sin(ATTRIBUTE[2ce4aad6]);\n" +
                "v1 = exists(ATTRIBUTE[3928f139]) ? float4(1,2,3,4) : float4(5,6,7,8);\n" +
                "v2 = cos(v0);\n" +
                "return v0+dot4(v1,ATTRIBUTE[0f7dd115].xyz)*v2;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         *   a = 10 * myvar;
         *   b = 11;
         *   return exists(myvar)
         *
         *
         * note the inclusion of 'return' plays no significance at all (the bytestream is identical)
         *   => return is always implied and mandatory
         *
         * In the places where myvar appears the identifier (51 A2 54 EA) is the same (it is the murmur32 of the string).
         * 0x19 retrieves its value and 0x1F retrieves its existence (true/false) or in float rep (1.0/0.0)
         *
         */
        [Test]
        public async Task TestDynamicExpression12()
        {
            var exampleStr = "07 00 00 20 41 19 51 A2 54 EA 15 08 00 07 00 00 30 41 08 01 1F 51 A2 54 EA 00";
            var expectedResult =
                "v0 = 10*ATTRIBUTE[ea54a251];\n" +
                "v1 = 11;\n" +
                "return exists(ATTRIBUTE[ea54a251]);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         *   a = 10 * myvar;
         *   return sin(exists(myvar)) ? (true ? (false ? 10 : frac(10*10)) : 1) : 1;
         *
         */
        [Test]
        public async Task TestDynamicExpression13()
        {
            var exampleStr =
            "07 00 00 20 41 19 51 A2 54 EA 15 08 00 1F 51 A2 54 EA 06 00 00 04 1A 00 4F 00 " +
            "07 00 00 80 3F 04 24 00 47 00 07 00 00 00 00 04 2E 00 36 00 07 00 00 20 41 02 " +
            "44 00 07 00 00 20 41 07 00 00 20 41 15 06 03 00 02 4C 00 07 00 00 80 3F 02 54 " +
            "00 07 00 00 80 3F 00";
            var expectedResult =
                "v0 = 10*ATTRIBUTE[ea54a251];\n" +
                "return sin(exists(ATTRIBUTE[ea54a251])) ? (1 ? (0 ? 10 : frac(10*10)) : 1) : 1;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * (.36*sin(1.2*time()))+.6
         *
         */
        [Test]
        public async Task TestDynamicExpression14()
        {
            var exampleStr =
            "07 EC 51 B8 3E 07 9A 99 99 3F 06 1B 00 15 06 00 00 15 07 9A 99 19 3F 13 00";
            var expectedResult = "return .36*sin(1.2*time())+.6;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestDynamicExpression15()
        {
            var exampleStr =
            "07 00 00 80 3F 04 0A 00 12 00 07 00 00 80 3F 02 17 00 07 00 00 00 40 00";
            var expectedResult = "return 1 || 2;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestDynamicExpression15_EnumMapper()
        {
            var exampleStr =
            "07 00 00 80 3F 04 0A 00 12 00 07 00 00 80 3F 02 17 00 07 00 00 00 40 00";

            // the condition is not a state value, so it is not mapped
            var expectedResult = "return 1 ? One : Two;";

            await Assert.That(new VfxEval(ParseString(exampleStr), enumMapper: Mapper).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * FEAT[0] && FEAT[1]
         */
        [Test]
        public async Task TestAndBranchWithEnumMapper()
        {
            var exampleStr = "1A 00 04 0F 00 07 00 07 00 00 00 00 02 11 00 1A 01 00";
            var expectedResult = "F_A && F_B";

            using (Assert.Multiple())
            {
                await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: ["F_A", "F_B"]).DynamicExpressionResult).IsEqualTo(expectedResult);
                await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: ["F_A", "F_B"], enumMapper: Mapper).DynamicExpressionResult).IsEqualTo(expectedResult);
            }
        }

        /*
         * (FEAT[0]==3) ? 5 : 2
         */
        [Test]
        public async Task TestEnumMapperSkipsComparisonLiteral()
        {
            var exampleStr = "1A 00 07 00 00 40 40 0D 04 0D 00 15 00 07 00 00 A0 40 02 1A 00 07 00 00 00 40 00";
            var expectedResult = "(F_A==3) ? Five : Two";

            await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: ["F_A"], enumMapper: Mapper).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * FEAT[0] && FEAT[1] && FEAT[2]
         */
        [Test]
        public async Task TestChainedAndBranches()
        {
            var exampleStr =
                "1A 00 04 0F 00 07 00 07 00 00 00 00 02 11 00 1A 01 " +
                "04 1E 00 16 00 07 00 00 00 00 02 20 00 1A 02 00";
            var expectedResult = "F_A && F_B && F_C";

            await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: ["F_A", "F_B", "F_C"]).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * F_ADDITIVE_BLENDING || 0
         *
         * results of 1 and 0 are where the short circuit and the conditional compile to the same
         * bytes, so this is written as the conditional it reads as, mapper or not
         */
        [Test]
        public async Task TestShortCircuitResultsAreNamedWithEnumMapper()
        {
            var exampleStr = "1A 00 04 07 00 0F 00 07 00 00 80 3F 02 14 00 07 00 00 00 00 00";

            static string boolMapper(int v) => v == 0 ? "false" : "true";

            using (Assert.Multiple())
            {
                await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: ["F_ADDITIVE_BLENDING"]).DynamicExpressionResult).IsEqualTo("F_ADDITIVE_BLENDING ? 1 : 0");
                await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: ["F_ADDITIVE_BLENDING"], enumMapper: boolMapper).DynamicExpressionResult).IsEqualTo("F_ADDITIVE_BLENDING ? true : false");
            }
        }

        /*
         * (0 || F_WIREFRAME || F_NO_CULLING || 1) ? 0 : 1
         *
         * only the two results of the outer conditional are values of the render state,
         * the short circuits are conditions and must stay unnamed
         */
        [Test]
        public async Task TestConditionIsNeverEnumMapped()
        {
            var exampleStr =
                "07 00 00 00 00 04 0A 00 12 00 07 00 00 80 3F 02 14 00 1A 02 " +
                "04 19 00 21 00 07 00 00 80 3F 02 23 00 1A 04 " +
                "04 28 00 30 00 07 00 00 80 3F 02 35 00 07 00 00 80 3F " +
                "04 3A 00 42 00 07 00 00 00 00 02 47 00 07 00 00 80 3F 00";

            string[] features = ["F_0", "F_1", "F_WIREFRAME", "F_3", "F_NO_CULLING"];

            static string cullModeMapper(int v)
            {
                return v switch
                {
                    0 => "None",
                    1 => "Back",
                    2 => "Front",
                    _ => "Unknown",
                };
            }

            using (Assert.Multiple())
            {
                await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: features).DynamicExpressionResult).IsEqualTo("(0 || F_WIREFRAME || F_NO_CULLING || 1) ? 0 : 1");
                await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: features, enumMapper: cullModeMapper).DynamicExpressionResult).IsEqualTo("(0 || F_WIREFRAME || F_NO_CULLING || 1) ? None : Back");
            }
        }

        [Test]
        public async Task TestFiveArgumentFunction()
        {
            // RemapVal(1,2,3,4,5)
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 07 00 00 40 40 07 00 00 80 40 07 00 00 A0 40 06 38 00 00";
            var expectedResult = "return RemapVal(1,2,3,4,5);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestSwizzleOnFunctionResult()
        {
            // sincos(1).xy
            var exampleStr = "07 00 00 80 3F 06 26 00 1E 54 00";
            var expectedResult = "return sincos(1).xy;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestStoreAndLoadAttribute()
        {
            // v0 = ATTRIBUTE[04030201]; return v0+v0;
            var exampleStr = "19 01 02 03 04 08 00 09 00 09 00 13 00";
            var expectedResult = "v0 = ATTRIBUTE[04030201];\nreturn v0+v0;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestLocalVariablesAreNamedInOrderOfUse()
        {
            // variable slots are not sequential, they are named in the order they are assigned
            var exampleStr = "07 00 00 80 3F 08 05 07 00 00 00 40 08 02 09 05 09 02 13 00";
            var expectedResult = "v0 = 1;\nv1 = 2;\nreturn v0+v1;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestFeatureIndexOutOfRange()
        {
            var exampleStr = "1A 05 00";
            await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: ["F_A"]).DynamicExpressionResult).IsEqualTo("FEAT[5]");
        }

        private static string Mapper(int v)
        {
            return v switch
            {
                1 => "One",
                2 => "Two",
                3 => "Three",
                5 => "Five",
                _ => "Unknown",
            };
        }

        [Test]
        public async Task TestDynamicExpression16()
        {
            var exampleStr =
            "07 00 00 00 00 00";
            var expectedResult = "return 0;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestDynamicExpression17()
        {
            var exampleStr =
             "07 00 00 20 41 08 00 07 00 00 30 41 08 01 07 00 00 A0 40 08 02 09 01 09 00 0F 04 1F 00 27 00 07 " +
             "00 00 80 3F 02 2C 00 09 02 09 00 0F 04 31 00 39 00 07 00 00 C8 42 02 3E 00 07 00 00 48 43 08 03 09 03 00";
            var expectedResult =
                "v0 = 10;\n" +
                "v1 = 11;\n" +
                "v2 = 5;\n" +
                "v3 = (v1>v0 || v2>v0) ? 100 : 200;\n" +
                "return v3;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * interpreting the opcodes 1A,1D in the shader code as COND and EVAL
         *
         */
        [Test]
        public async Task TestShaderDynamicExpression1()
        {
            var testInput1 = ParseString("1A 01 04 07 00 0F 00 07 00 00 80 3F 02 14 00 07 00 00 00 00 00");
            var expectedResultWithNoFeatures = "FEAT[1] ? 1 : 0";
            var expectedResultWithFeatures = "F_B ? 1 : 0";

            var testInput2 = ParseString("1D 3C 13 92 A3 1E A4 06 1F 00 00");
            var expectedResult2 = "SrgbGammaToLinear(MATERIAL_PARAM[a392133c].xyz)";

            using (Assert.Multiple())
            {
                await Assert.That(new VfxEval(testInput1, omitReturnStatement: true).DynamicExpressionResult).IsEqualTo(expectedResultWithNoFeatures);
                await Assert.That(new VfxEval(testInput1, omitReturnStatement: true, features: ["F_A", "F_B"]).DynamicExpressionResult).IsEqualTo(expectedResultWithFeatures);
                await Assert.That(new VfxEval(testInput2, omitReturnStatement: true).DynamicExpressionResult).IsEqualTo(expectedResult2);
            }
        }

        [Test]
        public async Task TestNestedTernary()
        {
            var nestedTernaryBin = ParseString(
                "1A 05 07 00 00 00 00 0D 04 0D 00 15 00 07 00 00 AA 42 02 59 00 " +
                "1A 05 07 00 00 80 3F 0D 04 22 00 2A 00 07 00 00 A0 41 02 59 00 " +
                "1A 05 07 00 00 00 40 0D 04 37 00 3F 00 07 00 00 A8 41 02 59 00 " +
                "1A 05 07 00 00 40 40 0D 04 4C 00 54 00 07 00 00 00 00 02 59 00 07 00 00 00 00 00");

            // (F_TEXTURE_FILTERING == 0 ? ANISOTROPIC : (F_TEXTURE_FILTERING == 1 ? BILINEAR : (F_TEXTURE_FILTERING == 2 ? TRILINEAR : (F_TEXTURE_FILTERING == 3 ? POINT : NEAREST))))
            var expectedResult = "(FEAT[5]==0) ? 85 : (FEAT[5]==1) ? 20 : (FEAT[5]==2) ? 21 : (FEAT[5]==3) ? 0 : 0";

            await Assert.That(new VfxEval(nestedTernaryBin, omitReturnStatement: true).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * recent functions
         *
         * v0 = rotation2d(12);
         * v1 = rotate2d(12,12);
         * v2 = sincos(10);
         * return v0;
         */
        [Test]
        public async Task TestDynamicExpression19()
        {
            var exampleStr = "07 00 00 40 41 06 24 00 08 00 07 00 00 40 41 07 00 00 40 41 06 25 00 08 01 07 00 00 20 41 06 26 00 08 02 09 00 00";
            var expectedResult =
                "v0 = rotation2d(12);\n" +
                "v1 = rotate2d(12,12);\n" +
                "v2 = sincos(10);\n" +
                "return v0;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * a = 0;
         * _27 = TextureSize(g_tColor);
         * _28 = TextureAverageColor(g_tColor);
         * _29 = MatrixIdentity();
         * _2A = MatrixScale(float3(0.5, 0.3, 0.2));
         * _2B = MatrixTranslate(float3(1.0, 2.0, 3.0));
         * _2C = MatrixAxisAngle(float4(0.0, 1.0, 0.0, 45.0));
         * _2D = MatrixAxisToAxis(float3(1.0, 0.0, 0.0), float3(0.0, 1.0, 0.0));
         * _2E = MatrixMultiply(_2A, _2B);
         * _2F = MatrixColorCorrect(float4(1.0, 0.8, 0.6, 1.0)); // contrast, saturation, brightness, unknown
         * _30 = MatrixColorCorrect2(float4(1.0, 0.8, 0.6, 1.0), TextureAverageColor(g_tColor));
         * _31 = MatrixColorTint(float4(1.0, 1.0, 1.0, 1.0));
         *
         * return a;
         */

        [Test]
        public async Task TestDynamicExpression18_Matrices()
        {
            var blob = ParseString(
                "07 00 00 00 00 08 00 19 0F 54 63 59 06 27 00 08 01 19 0F 54 63 59 06 28 00 08 02 06 29 00 08 03 " +
                "07 00 00 00 3F 07 9A 99 99 3E 07 CD CC 4C 3E 06 19 00 06 2A 00 08 04 07 00 00 80 3F 07 00 00 00 " +
                "40 07 00 00 40 40 06 19 00 06 2B 00 08 05 07 00 00 00 00 07 00 00 80 3F 07 00 00 00 00 07 00 00 " +
                "34 42 06 18 00 06 2C 00 08 06 07 00 00 80 3F 07 00 00 00 00 07 00 00 00 00 06 19 00 07 00 00 00 " +
                "00 07 00 00 80 3F 07 00 00 00 00 06 19 00 06 2D 00 08 07 09 04 09 05 06 2E 00 08 08 07 00 00 80 " +
                "3F 07 CD CC 4C 3F 07 9A 99 19 3F 07 00 00 80 3F 06 18 00 06 2F 00 08 09 07 00 00 80 3F 07 CD CC " +
                "4C 3F 07 9A 99 19 3F 07 00 00 80 3F 06 18 00 19 0F 54 63 59 06 28 00 06 30 00 08 0A 07 00 00 80 " +
                "3F 07 00 00 80 3F 07 00 00 80 3F 07 00 00 80 3F 06 18 00 06 31 00 08 0B 09 00 00"
            );

            var expectedResult = "v0 = 0;\n" +
                "v1 = TextureSize(g_tColor);\n" +
                "v2 = TextureAverageColor(g_tColor);\n" +
                "v3 = MatrixIdentity();\n" +
                "v4 = MatrixScale(float3(.5,.3,.2));\n" +
                "v5 = MatrixTranslate(float3(1,2,3));\n" +
                "v6 = MatrixAxisAngle(float4(0,1,0,45));\n" +
                "v7 = MatrixAxisToAxis(float3(1,0,0),float3(0,1,0));\n" +
                "v8 = MatrixMultiply(v4,v5);\n" +
                "v9 = MatrixColorCorrect(float4(1,.8,.6,1));\n" +
                "v10 = MatrixColorCorrect2(float4(1,.8,.6,1),TextureAverageColor(g_tColor));\n" +
                "v11 = MatrixColorTint(float4(1,1,1,1));\n" +
                "return v0;";

            await Assert.That(new VfxEval(blob, ["g_tColor"]).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * 1+2+3+4
         *
         * the bytecode is left nested and so is the reading of the output, no brackets needed
         *
         */
        [Test]
        public async Task TestDynamicExpression20()
        {
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 13 07 00 00 40 40 13 07 00 00 80 40 13 00";
            var expectedResult =
                "return 1+2+3+4;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        /*
         * malformed expression, reader will throw System.IO.EndOfStreamException
         *
         */
        [Test]
        public void TestDynamicExpression21()
        {
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 13 07 00 00 40 40 13 07 00";
            Assert.ThrowsExactly<EndOfStreamException>(() => _ = new VfxEval(ParseString(exampleStr)));
        }

        [Test]
        public async Task TestShaderDynamicExpression2()
        {
            var testInput = ParseString(
                "1A 13 04 0F 00 07 00 07 00 00 00 00 02 14 00 1F 28 A6 90 70 04 19 00 21 00 19 A1 D0 52 1E 02 26 00 1D 6F 89 29 B8 00");

            // parsing a shader registers its variable names, which is what resolves this one
            StringToken.Store("g_flReflectionsTintByBaseBlendToNone");

            var expectedResult = "(FEAT[19] && exists($reflectionstintbybaseblendtonone)) ? ATTRIBUTE[1e52d0a1] : g_flReflectionsTintByBaseBlendToNone";
            var vfxEval = new VfxEval(testInput, omitReturnStatement: true);
            await Assert.That(vfxEval.DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestShaderDynamicExpression3()
        {
            var testInput = ParseString(
                "1D 37 2B 32 AB 07 DB 0F 49 40 15 07 00 00 34 43 16 08 00 09 00 06 01 00 1D D2 F6 9A C7 16 08 01 09 " +
                "00 06 00 00 1D D2 F6 9A C7 16 08 02 1D 16 82 0D 28 1D 16 82 0D 28 06 0B 00 07 AC C5 27 37 0F 04 45 " +
                "00 51 00 1D 16 82 0D 28 06 1B 00 15 02 56 00 07 00 00 00 00 1D CF 75 4A D4 13 07 00 00 00 3F 09 01 " +
                "09 02 14 09 02 09 01 13 06 1A 00 15 14 07 00 00 00 3F 13 08 03 09 02 09 01 09 03 1E 55 06 19 00 00");
            var expectedResult =
                "v0 = MATERIAL_PARAM[ab322b37]*3.1415927/180;\n" +
                "v1 = cos(v0)/MATERIAL_PARAM[c79af6d2];\n" +
                "v2 = sin(v0)/MATERIAL_PARAM[c79af6d2];\n" +
                "v3 = ((dot2(MATERIAL_PARAM[280d8216],MATERIAL_PARAM[280d8216])>1e-05) ? MATERIAL_PARAM[280d8216]*time() : 0)+MATERIAL_PARAM[d44a75cf]-.5*float2(v1-v2,v2+v1)+.5;\n" +
                "float3(v2,v1,v3.y)";
            await Assert.That(new VfxEval(testInput, omitReturnStatement: true).DynamicExpressionResult).IsEqualTo(expectedResult);
        }


        [Test]
        public async Task TestMatrixColorTint2_GrayInput()
        {
            // Gray has zero saturation, result should be ~identity
            var result = VfxEvalFunctions.MatrixColorTint2(new Vector3(0.5f, 0.5f, 0.5f), 1.0f);
            await AssertMatrixEqual(Matrix4x4.Identity, result, 1e-5f);
        }

        [Test]
        public async Task TestMatrixColorTint2_WhiteInput()
        {
            // White has zero saturation, result should be ~identity
            var result = VfxEvalFunctions.MatrixColorTint2(new Vector3(1f, 1f, 1f), 1.0f);
            await AssertMatrixEqual(Matrix4x4.Identity, result, 1e-5f);
        }

        [Test]
        public async Task TestMatrixColorTint2_PureRed()
        {
            var result = VfxEvalFunctions.MatrixColorTint2(new Vector3(1f, 0f, 0f), 1.0f);
            var expected = new Matrix4x4(
                0f, 0f, 0f, 0.99999994f,
                0f, 0f, 0f, 5.3124536E-09f,
                0f, 0f, 0f, -2.9802322E-08f,
                0f, 0f, 0f, 1f
            );
            await AssertMatrixEqual(expected, result, 1e-5f);
        }

        [Test]
        public async Task TestMatrixColorTint2_WarmColor()
        {
            var result = VfxEvalFunctions.MatrixColorTint2(new Vector3(0.8f, 0.2f, 0.1f), 0.5f);
            var expected = new Matrix4x4(
                0.16458952f, 0.44803202f, 0.0045659216f, 0.57826537f,
                0.039589547f, 0.57303196f, 0.0045659216f, 0.053265363f,
                0.039589554f, 0.44803208f, 0.12956592f, -0.034234628f,
                0f, 0f, 0f, 1f
            );
            await AssertMatrixEqual(expected, result, 1e-5f);
        }

        [Test]
        public async Task TestMatrixColorTint2_BluishColor()
        {
            var result = VfxEvalFunctions.MatrixColorTint2(new Vector3(0.3f, 0.6f, 0.9f), 1.0f);
            var expected = new Matrix4x4(
                0.351208f, 0.20228605f, 0.0020615086f, 0.07141057f,
                0.017874645f, 0.5356194f, 0.00206151f, 0.27141058f,
                0.01787465f, 0.20228608f, 0.3353949f, 0.47141054f,
                0f, 0f, 0f, 1f
            );
            await AssertMatrixEqual(expected, result, 1e-5f);
        }

        [Test]
        public async Task TestMatrixColorCorrect2_Identity()
        {
            // contrast=1, saturation=1, brightness=1 should be ~identity
            var result = VfxEvalFunctions.MatrixColorCorrect2(new Vector3(1f, 1f, 1f), new Vector3(0.5f, 0.5f, 0.5f));
            await AssertMatrixEqual(Matrix4x4.Identity, result, 1e-5f);
        }

        [Test]
        public async Task TestMatrixColorCorrect2_Adjusted()
        {
            var result = VfxEvalFunctions.MatrixColorCorrect2(new Vector3(1.2f, 0.8f, 1.5f), new Vector3(0.3f, 0.4f, 0.5f));
            var expected = new Matrix4x4(
                1.468957f, 0.32770345f, 0.003339633f, -0.095573045f,
                0.028956933f, 1.7677034f, 0.0033396427f, -0.11957306f,
                0.02895683f, 0.32770318f, 1.4433398f, -0.14357306f,
                0f, 0f, 0f, 1f
            );
            await AssertMatrixEqual(expected, result, 1e-5f);
        }

        [Test]
        public async Task TestNotOpcode()
        {
            // !1
            var exampleStr = "07 00 00 80 3F 0C 00";
            var expectedResult = "return !1;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestNegateOpcode()
        {
            // -5
            var exampleStr = "07 00 00 A0 40 18 00";
            var expectedResult = "return -5;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestDoubleNegate()
        {
            // --3
            var exampleStr = "07 00 00 40 40 18 18 00";
            var expectedResult = "return --3;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestModuloOpcode()
        {
            // 10 % 3
            var exampleStr = "07 00 00 20 41 07 00 00 40 40 17 00";
            var expectedResult = "return 10%3;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestDivisionOpcode()
        {
            // 10 / 2
            var exampleStr = "07 00 00 20 41 07 00 00 00 40 16 00";
            var expectedResult = "return 10/2;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestMixedOperatorPrecedence()
        {
            // ((1*2)+3)-4
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 15 07 00 00 40 40 13 07 00 00 80 40 14 00";
            var expectedResult = "return 1*2+3-4;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestMixedOperatorsAddMul()
        {
            // (1+2) - (3*4)
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 13 07 00 00 40 40 07 00 00 80 40 15 14 00";
            var expectedResult = "return 1+2-3*4;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestNotWithComparison()
        {
            // !(1>2)
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 0F 0C 00";
            var expectedResult = "return !(1>2);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestNegateWithAddition()
        {
            // -1 + 2
            var exampleStr = "07 00 00 80 3F 18 07 00 00 00 40 13 00";
            var expectedResult = "return -1+2;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestComparisonOperatorChain()
        {
            // (1==2) != (3>=4)
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 0D 07 00 00 40 40 07 00 00 80 40 10 0E 00";
            var expectedResult = "return (1==2)!=(3>=4);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestComplexNotExpression()
        {
            // !((1+2) > (3*4))
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 13 07 00 00 40 40 07 00 00 80 40 15 0F 0C 00";
            var expectedResult = "return !(1+2>3*4);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestNestedFunctionCalls()
        {
            // cos(sin(10))
            var exampleStr = "07 00 00 20 41 06 00 00 06 01 00 00";
            var expectedResult = "return cos(sin(10));";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestLerpFunction()
        {
            // lerp(1, 2, .5)
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 07 00 00 00 3F 06 08 00 00";
            var expectedResult = "return lerp(1,2,.5);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestClampFunction()
        {
            // clamp(10, 0, 100)
            var exampleStr = "07 00 00 20 41 07 00 00 00 00 07 00 00 C8 42 06 07 00 00";
            var expectedResult = "return clamp(10,0,100);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestSaturateFunction()
        {
            // saturate(.5)
            var exampleStr = "07 00 00 00 3F 06 06 00 00";
            var expectedResult = "return saturate(.5);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestSwizzleW()
        {
            // ATTRIBUTE.w
            var exampleStr = "19 01 02 03 04 1E FF 00";
            var expectedResult = "return ATTRIBUTE[04030201].w;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestSwizzleXyzw()
        {
            // ATTRIBUTE.xyzw
            var exampleStr = "19 01 02 03 04 1E E4 00";
            var expectedResult = "return ATTRIBUTE[04030201].xyzw;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestSwizzleXy()
        {
            // ATTRIBUTE.xy (packed as xyyy, trimmed to xy)
            var exampleStr = "19 01 02 03 04 1E 54 00";
            var expectedResult = "return ATTRIBUTE[04030201].xy;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestSwizzleX()
        {
            // ATTRIBUTE.x (packed as xxxx, trimmed to x)
            var exampleStr = "19 01 02 03 04 1E 00 00";
            var expectedResult = "return ATTRIBUTE[04030201].x;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestMultipleStoreLoad()
        {
            // v0 = 1; v1 = 2; return v0+v1;
            var exampleStr = "07 00 00 80 3F 08 00 07 00 00 00 40 08 01 09 00 09 01 13 00";
            var expectedResult = "v0 = 1;\nv1 = 2;\nreturn v0+v1;";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestFeatureWithNames()
        {
            // FEATURE[0] with feature names
            var exampleStr = "1A 00 00";
            await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true, features: ["MY_FEATURE", "OTHER"]).DynamicExpressionResult).IsEqualTo("MY_FEATURE");
        }

        [Test]
        public async Task TestFeatureWithoutNames()
        {
            // FEATURE[2] without feature names
            var exampleStr = "1A 02 00";
            await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true).DynamicExpressionResult).IsEqualTo("FEAT[2]");
        }

        [Test]
        public async Task TestMaterialParam()
        {
            // MATERIAL_PARAM[04030201]
            var exampleStr = "1D 01 02 03 04 00";
            var expectedResult = "return MATERIAL_PARAM[04030201];";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestExistsStandalone()
        {
            // exists(ATTRIBUTE[04030201])
            var exampleStr = "1F 01 02 03 04 00";
            var expectedResult = "return exists(ATTRIBUTE[04030201]);";
            await Assert.That(new VfxEval(ParseString(exampleStr)).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public async Task TestOmitReturnStatement()
        {
            // 1+2 with omitReturnStatement
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 13 00";
            var expectedResult = "1+2";
            await Assert.That(new VfxEval(ParseString(exampleStr), omitReturnStatement: true).DynamicExpressionResult).IsEqualTo(expectedResult);
        }

        [Test]
        public void TestUnknownOpcodeThrows()
        {
            // NOP (0x01) has no handler
            var exampleStr = "01";
            Assert.ThrowsExactly<InvalidDataException>(() => _ = new VfxEval(ParseString(exampleStr)));
        }

        [Test]
        public void TestInsufficientExpressionsForBinaryOp()
        {
            // Only one float, then ADD which needs two
            var exampleStr = "07 00 00 80 3F 13";
            Assert.ThrowsExactly<InvalidDataException>(() => _ = new VfxEval(ParseString(exampleStr)));
        }

        [Test]
        public void TestReturnWithRemainingDataThrows()
        {
            // RETURN but there's still data after
            var exampleStr = "07 00 00 80 3F 00 07";
            Assert.ThrowsExactly<InvalidDataException>(() => _ = new VfxEval(ParseString(exampleStr)));
        }

        [Test]
        public void TestInvalidFunctionIdThrows()
        {
            // FUNC with out-of-range function id (0xFF)
            var exampleStr = "06 FF 00";
            Assert.ThrowsExactly<InvalidDataException>(() => _ = new VfxEval(ParseString(exampleStr)));
        }

        [Test]
        public void TestEmptyExpressionStackThrows()
        {
            // STORE, NEGATE and RETURN with nothing on the expression stack
            using (Assert.Multiple())
            {
                Assert.ThrowsExactly<InvalidDataException>(() => _ = new VfxEval(ParseString("08 00")));
                Assert.ThrowsExactly<InvalidDataException>(() => _ = new VfxEval(ParseString("18 00")));
                Assert.ThrowsExactly<InvalidDataException>(() => _ = new VfxEval(ParseString("00")));
            }
        }

        [Test]
        public void TestMalformedFunctionSignatureThrows()
        {
            // FUNC with non-zero check byte
            var exampleStr = "07 00 00 80 3F 06 00 01";
            Assert.ThrowsExactly<InvalidDataException>(() => _ = new VfxEval(ParseString(exampleStr)));
        }

        private static async Task AssertMatrixEqual(Matrix4x4 expected, Matrix4x4 actual, float tolerance)
        {
            using (Assert.Multiple())
            {
                await Assert.That(actual.M11).IsEqualTo(expected.M11).Within(tolerance);
                await Assert.That(actual.M12).IsEqualTo(expected.M12).Within(tolerance);
                await Assert.That(actual.M13).IsEqualTo(expected.M13).Within(tolerance);
                await Assert.That(actual.M14).IsEqualTo(expected.M14).Within(tolerance);
                await Assert.That(actual.M21).IsEqualTo(expected.M21).Within(tolerance);
                await Assert.That(actual.M22).IsEqualTo(expected.M22).Within(tolerance);
                await Assert.That(actual.M23).IsEqualTo(expected.M23).Within(tolerance);
                await Assert.That(actual.M24).IsEqualTo(expected.M24).Within(tolerance);
                await Assert.That(actual.M31).IsEqualTo(expected.M31).Within(tolerance);
                await Assert.That(actual.M32).IsEqualTo(expected.M32).Within(tolerance);
                await Assert.That(actual.M33).IsEqualTo(expected.M33).Within(tolerance);
                await Assert.That(actual.M34).IsEqualTo(expected.M34).Within(tolerance);
                await Assert.That(actual.M41).IsEqualTo(expected.M41).Within(tolerance);
                await Assert.That(actual.M42).IsEqualTo(expected.M42).Within(tolerance);
                await Assert.That(actual.M43).IsEqualTo(expected.M43).Within(tolerance);
                await Assert.That(actual.M44).IsEqualTo(expected.M44).Within(tolerance);
            }
        }

        private static byte[] ParseString(string bytestring)
        {
            var tokens = bytestring.Split(" ");
            var databytes = new byte[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                databytes[i] = Convert.ToByte(tokens[i], 16);
            }
            return databytes;
        }
    }
}
