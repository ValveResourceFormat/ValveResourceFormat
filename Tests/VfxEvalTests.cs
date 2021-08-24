using System;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat.Serialization.VfxEval;

namespace Tests
{
    public class VfxEvalTests
    {
        /*
         * random(1,2)
         */
        [Test]
        public void TestDynamicExpression1()
        {
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 06 20 00 00";
            var expectedResult = "return random(1,2);";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        [Test]
        public void TestDynamicExpression2()
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
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        [Test]
        public void TestDynamicExpression3()
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
                "v0 = UNKNOWN>UNKNOWN2;\n" +
                "v0 = UNKNOWN<UNKNOWN2;\n" +
                "v0 = UNKNOWN==UNKNOWN2;\n" +
                "v0 = UNKNOWN>=UNKNOWN2;\n" +
                "v0 = UNKNOWN<=UNKNOWN2;\n" +
                "v0 = UNKNOWN!=UNKNOWN2;\n" +
                "return frac(v0);";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
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
        public void TestDynamicExpression4()
        {
            var exampleStr = "07 00 00 80 3F 04 0A 00 12 00 07 00 00 00 40 02 17 00 07 00 00 40 40 00";
            var expectedResult = "return 1 ? 2 : 3;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         *      a = length(1);
         *      a = sqrt(1);
         *      a = TextureSize(1);
         *      frac(a)
         */
        [Test]
        public void TestDynamicExpression5()
        {
            var exampleStr = "07 00 00 80 3F 06 22 00 08 00 07 00 00 80 3F 06 11 00 08 00 07 00 00 80 3F 06 24 00 08 00 09 00 06 03 00 00";
            var expectedResult =
                "v0 = length(1);\n" +
                "v0 = sqrt(1);\n" +
                "v0 = TextureSize(1);\n" +
                "return frac(v0);";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        [Test]
        public void TestDynamicExpression6()
        {
            var exampleStr = "07 00 00 80 3F 07 00 00 80 40 13 08 00 07 00 00 20 41 07 00 00 20 42 13 00";
            var expectedResult = "v0 = 1+4;\nreturn 10+40;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         * true ? (true ? 1 : 2) : 123
         *
         * true will be replaced by 1
         *
         */
        [Test]
        public void TestDynamicExpression7()
        {
            var exampleStr = "07 00 00 80 3F 04 0A 00 24 00 07 00 00 80 3F 04 14 00 1C 00 07 00 00 80 3F " +
                "02 21 00 07 00 00 00 40 02 29 00 07 00 00 F6 42 00";
            var expectedResult = "return 1 ? (1 || 2) : 123;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         * v1 && v2 ? frac(10) : 100*100
         *
         * the expression is formed like this
         * EXT ? 0 : EXT1 ? frac(10) : 100*100
         *
         * At the branch-operation we check for the very specific byte pattern
         * 12 and 0A will vary, however 12-0A will always be 8 (the length of
         *
         *          04 12 00 0A 00 07 00 00 00 00
         *
         */
        [Test]
        public void TestDynamicExpression8()
        {
            var exampleStr = "19 38 AE 48 52 04 12 00 0A 00 07 00 00 00 00 02 17 00 19 31 FB FD 02 04 1C 00 27 00 07 00 00 20 " +
               "41 06 03 00 02 32 00 07 00 00 C8 42 07 00 00 C8 42 15 00";
            var expectedResult = "return (UNKNOWN && UNKNOWN2) ? frac(10) : (100*100);";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         * a = 10+10;
         * v1||v2 ? sin(1) : 7
         *
         */
        [Test]
        public void TestDynamicExpression9()
        {
            var exampleStr = "07 00 00 20 41 07 00 00 20 41 13 08 00 19 38 AE 48 52 04 17 00 1F 00 07 00 00 80 3F 02 24 00 19 " +
                "31 FB FD 02 04 29 00 34 00 07 00 00 80 3F 06 00 00 02 39 00 07 00 00 E0 40 00";
            var expectedResult = "v0 = 10+10;\nreturn (UNKNOWN || UNKNOWN2) ? sin(1) : 7;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
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
        public void TestDynamicExpression10()
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
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         *  v0 = sin(EXT);
         *  v1 = exists(EXT2) ? float4(1,2,3,4) : float4(5,6,7,8);
         *  v2 = cos(v0);
         *  return v0+(dot4(v1,EXT3.xyz)*v2);
         *
         */
        [Test]
        public void TestDynamicExpression11()
        {
            var exampleStr =
            "19 D6 AA E4 2C 06 00 00 08 00 1F 39 F1 28 39 04 14 00 2E 00 07 00 00 80 3F 07 00 00 00 40 07 00 " +
            "00 40 40 07 00 00 80 40 06 18 00 02 45 00 07 00 00 A0 40 07 00 00 C0 40 07 00 00 E0 40 07 00 00 " +
            "00 41 06 18 00 08 01 09 00 06 01 00 08 02 09 00 09 01 19 15 D1 7D 0F 1E A4 06 09 00 09 02 15 13 00";
            var expectedResult =
                "v0 = sin(UNKNOWN);\n" +
                "v1 = exists(UNKNOWN2) ? float4(1,2,3,4) : float4(5,6,7,8);\n" +
                "v2 = cos(v0);\n" +
                "return v0+(dot4(v1,UNKNOWN3.xyz)*v2);";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
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
         * 0x19 retrieves its value and 0x1F retrives it's existence (true/false) or in float rep (1.0/0.0)
         *
         */
        [Test]
        public void TestDynamicExpression12()
        {
            var exampleStr = "07 00 00 20 41 19 51 A2 54 EA 15 08 00 07 00 00 30 41 08 01 1F 51 A2 54 EA 00";
            var expectedResult = "v0 = 10*UNKNOWN;\n" +
                "v1 = 11;\n" +
                "return exists(UNKNOWN);";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         *   a = 10 * myvar;
         *   return sin(exists(myvar)) ? (true ? (false ? 10 : frac(10*10)) : 1) : 1;
         *
         */
        [Test]
        public void TestDynamicExpression13()
        {
            var exampleStr =
            "07 00 00 20 41 19 51 A2 54 EA 15 08 00 1F 51 A2 54 EA 06 00 00 04 1A 00 4F 00 " +
            "07 00 00 80 3F 04 24 00 47 00 07 00 00 00 00 04 2E 00 36 00 07 00 00 20 41 02 " +
            "44 00 07 00 00 20 41 07 00 00 20 41 15 06 03 00 02 4C 00 07 00 00 80 3F 02 54 " +
            "00 07 00 00 80 3F 00";
            var expectedResult =
                "v0 = 10*UNKNOWN;\n" +
                "return sin(exists(UNKNOWN)) ? (1 ? (0 ? 10 : frac(10*10)) : 1) : 1;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         * (.36*sin(1.2*time()))+.6
         *
         */
        [Test]
        public void TestDynamicExpression14()
        {
            var exampleStr =
            "07 EC 51 B8 3E 07 9A 99 99 3F 06 1B 00 15 06 00 00 15 07 9A 99 19 3F 13 00";
            var expectedResult = "return (.36*sin(1.2*time()))+.6;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        [Test]
        public void TestDynamicExpression15()
        {
            var exampleStr =
            "07 00 00 80 3F 04 0A 00 12 00 07 00 00 80 3F 02 17 00 07 00 00 00 40 00";
            var expectedResult = "return 1 || 2;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        [Test]
        public void TestDynamicExpression16()
        {
            var exampleStr =
            "07 00 00 00 00 00";
            var expectedResult = "return 0;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        [Test]
        public void TestDynamicExpression17()
        {
            var exampleStr =
             "07 00 00 20 41 08 00 07 00 00 30 41 08 01 07 00 00 A0 40 08 02 09 01 09 00 0F 04 1F 00 27 00 07 " +
             "00 00 80 3F 02 2C 00 09 02 09 00 0F 04 31 00 39 00 07 00 00 C8 42 02 3E 00 07 00 00 48 43 08 03 09 03 00";
            var expectedResult = "v0 = 10;\nv1 = 11;\nv2 = 5;\nv3 = ((v1>v0) || (v2>v0)) ? 100 : 200;\nreturn v3;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         * interpreting the opcodes 1A,1D in the shader code as COND and EVAL
         *
         */
        [Test]
        public void TestShaderDynamicExpression1()
        {
            var testInput1 = ParseString("1A 11 04 07 00 0F 00 07 00 00 80 3F 02 14 00 07 00 00 00 00 00");
            var expectedResult1 = "COND[17] || 0";
            Assert.AreEqual(expectedResult1, new VfxEval(testInput1, omitReturnStatement: true).DynamicExpressionResult);
            var testInput2 = ParseString("1D 3C 13 92 A3 1E A4 06 1F 00 00");
            var expectedResult2 = "SrgbGammaToLinear(EVAL[a392133c].xyz)";
            Assert.AreEqual(expectedResult2, new VfxEval(testInput2, omitReturnStatement: true).DynamicExpressionResult);
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
        public void TestDynamicExpression19()
        {
            var exampleStr = "07 00 00 40 41 06 25 00 08 00 07 00 00 40 41 07 00 00 40 41 06 26 00 08 01 07 00 00 20 41 06 27 00 08 02 09 00 00";
            var expectedResult =
                "v0 = rotation2d(12);\n" +
                "v1 = rotate2d(12,12);\n" +
                "v2 = sincos(10);\n" +
                "return v0;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         * 1+2+3+4
         *
         * is decompiled as
         *
         * ((1+2)+3)+4
         *
         */
        [Test]
        public void TestDynamicExpression20()
        {
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 13 07 00 00 40 40 13 07 00 00 80 40 13 00";
            var expectedResult =
                "return ((1+2)+3)+4;";
            Assert.AreEqual(expectedResult, new VfxEval(ParseString(exampleStr)).DynamicExpressionResult);
        }

        /*
         * malformed expression, reader will throw System.IO.EndOfStreamException (which is caught)
         *
         */
        [Test]
        public void TestDynamicExpression21()
        {
            var exampleStr = "07 00 00 80 3F 07 00 00 00 40 13 07 00 00 40 40 13 07 00";
            Assert.Throws<EndOfStreamException>(() => new VfxEval(ParseString(exampleStr)));
        }

        [Test]
        public void TestShaderDynamicExpression2()
        {
            var testInput = ParseString(
                "1A 13 04 0F 00 07 00 07 00 00 00 00 02 14 00 1F 28 A6 90 70 04 19 00 21 00 19 A1 D0 52 1E 02 26 00 1D 6F 89 29 B8 00");
            var expectedResult = "(COND[19] && exists(UNKNOWN[7090a628])) ? UNKNOWN[1e52d0a1] : EVAL[b829896f]";
            VfxEval vfxEval = new VfxEval(testInput, omitReturnStatement: true, showMurmurForUnknowns: true);
            Assert.AreEqual(expectedResult, vfxEval.DynamicExpressionResult);
        }

        [Test]
        public void TestShaderDynamicExpression3()
        {
            var testInput = ParseString(
                "1D 37 2B 32 AB 07 DB 0F 49 40 15 07 00 00 34 43 16 08 00 09 00 06 01 00 1D D2 F6 9A C7 16 08 01 09 " +
                "00 06 00 00 1D D2 F6 9A C7 16 08 02 1D 16 82 0D 28 1D 16 82 0D 28 06 0B 00 07 AC C5 27 37 0F 04 45 " +
                "00 51 00 1D 16 82 0D 28 06 1B 00 15 02 56 00 07 00 00 00 00 1D CF 75 4A D4 13 07 00 00 00 3F 09 01 " +
                "09 02 14 09 02 09 01 13 06 1A 00 15 14 07 00 00 00 3F 13 08 03 09 02 09 01 09 03 1E 55 06 19 00 00");
            var expectedResult =
                "v0 = (EVAL[ab322b37]*3.1415927)/180;\n" +
                "v1 = cos(v0)/EVAL[c79af6d2];\n" +
                "v2 = sin(v0)/EVAL[c79af6d2];\n" +
                "v3 = ((((dot2(EVAL[280d8216],EVAL[280d8216])>1e-05) ? (EVAL[280d8216]*time()) : 0)+EVAL[d44a75cf])-(.5*float2(v1-v2,v2+v1)))+.5;\n" +
                "float3(v2,v1,v3.y)";
            Assert.AreEqual(expectedResult, new VfxEval(testInput, omitReturnStatement: true).DynamicExpressionResult);
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
