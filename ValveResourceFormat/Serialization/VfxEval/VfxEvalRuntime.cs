using System.Buffers.Binary;
using ValveResourceFormat.Utils;
using Function = ValveResourceFormat.Serialization.VfxEval.VfxEval.ExpressionFunction;
using OPCODE = ValveResourceFormat.Serialization.VfxEval.VfxEval.OPCODE;

namespace ValveResourceFormat.Serialization.VfxEval
{
    /// <summary>
    /// Supplies the values a compiled dynamic expression reads.
    /// </summary>
    public interface IVfxEvalContext
    {
        /// <summary>Gets the seconds the <c>time</c> function returns.</summary>
        public float Time => 0f;

        /// <summary>Gets a scalar or vector material parameter, with scalars in every component.</summary>
        public bool TryGetMaterialParam(StringToken name, out Vector4 value);

        /// <summary>Gets whether a material parameter is a texture.</summary>
        public bool IsTextureParam(StringToken name) => false;

        /// <summary>Gets a render attribute, with scalars in every component.</summary>
        public bool TryGetAttribute(StringToken name, out Vector4 value)
        {
            value = default;
            return false;
        }

        /// <summary>Gets the value of a feature by its index in the features file.</summary>
        public int GetFeature(int index) => 0;

        /// <summary>Gets the size of a texture parameter.</summary>
        public Vector4 GetTextureSize(StringToken textureName) => Vector4.Zero;

        /// <summary>Gets the average color of a texture parameter.</summary>
        public Vector4 GetTextureAverageColor(StringToken textureName) => Vector4.UnitW;
    }

    /// <summary>
    /// Runs compiled dynamic expressions, the counterpart of <see cref="VfxEval"/> which decompiles them.
    /// </summary>
    public static class VfxEvalRuntime
    {
        /// <summary>Runs an expression that results in a vector or scalar, failing on an unset attribute, an unknown parameter or an unsupported operation.</summary>
        public static bool TryEvaluate(ReadOnlySpan<byte> bytecode, IVfxEvalContext context, out Vector4 value, out bool isConstant)
        {
            var stack = new ValueStack(stackalloc Vector4[MaxStackDepth]);

            if (Run(bytecode, context, ref stack, out isConstant) && stack.Count == 1)
            {
                value = stack.Pop();
                return true;
            }

            value = default;
            return false;
        }

        /// <inheritdoc cref="TryEvaluate"/>
        public static bool TryEvaluateMatrix(ReadOnlySpan<byte> bytecode, IVfxEvalContext context, out Matrix4x4 value, out bool isConstant)
        {
            var stack = new ValueStack(stackalloc Vector4[MaxStackDepth]);

            if (Run(bytecode, context, ref stack, out isConstant) && stack.Count == 4)
            {
                value = PopMatrix(ref stack);
                return true;
            }

            value = default;
            return false;
        }

        private const int MaxStackDepth = 32;

        private ref struct ValueStack(Span<Vector4> items)
        {
            private readonly Span<Vector4> items = items;

            public int Count { get; private set; }

            /// <summary>Gets whether a push found the stack full or a pop found it empty, failing the evaluation.</summary>
            public bool Failed { get; private set; }

            public void Push(Vector4 value)
            {
                if (Count == items.Length)
                {
                    Failed = true;
                    return;
                }

                items[Count++] = value;
            }

            public Vector4 Pop()
            {
                if (Count == 0)
                {
                    Failed = true;
                    return default;
                }

                return items[--Count];
            }
        }

        private static bool Run(ReadOnlySpan<byte> bytecode, IVfxEvalContext context, ref ValueStack stack, out bool isConstant)
        {
            isConstant = true;

            // Locals are addressed by a byte
            Span<Vector4> locals = stackalloc Vector4[256];
            Span<int> callStack = stackalloc int[32];
            var callStackTop = 0;
            var offset = 0;

            while (offset < bytecode.Length && !stack.Failed)
            {
                var opcode = (OPCODE)bytecode[offset++];

                switch (opcode)
                {
                    case OPCODE.RETURN:
                        return !stack.Failed;

                    case OPCODE.NOP:
                        break;

                    case OPCODE.JUMP:
                        offset = BinaryPrimitives.ReadUInt16LittleEndian(bytecode[offset..]);
                        break;

                    case OPCODE.CALL:
                        if (callStackTop == callStack.Length)
                        {
                            return false;
                        }

                        callStack[callStackTop++] = offset + 2;
                        offset = BinaryPrimitives.ReadUInt16LittleEndian(bytecode[offset..]);
                        break;

                    case OPCODE.RET:
                        if (callStackTop == 0)
                        {
                            return false;
                        }

                        offset = callStack[--callStackTop];
                        break;

                    case OPCODE.BRANCH:
                    {
                        var whenTrue = BinaryPrimitives.ReadUInt16LittleEndian(bytecode[offset..]);
                        var whenFalse = BinaryPrimitives.ReadUInt16LittleEndian(bytecode[(offset + 2)..]);
                        offset = stack.Pop() != Vector4.Zero ? whenTrue : whenFalse;
                        break;
                    }

                    case OPCODE.FUNC:
                    {
                        var function = BinaryPrimitives.ReadUInt16LittleEndian(bytecode[offset..]);
                        offset += 2;

                        isConstant &= (Function)function is not (Function.Time or Function.Random);

                        if (!CallFunction((Function)function, ref stack, context))
                        {
                            return false;
                        }

                        break;
                    }

                    case OPCODE.FLOAT:
                        stack.Push(new Vector4(BinaryPrimitives.ReadSingleLittleEndian(bytecode[offset..])));
                        offset += 4;
                        break;

                    case OPCODE.FLOAT4:
                        stack.Push(new Vector4(
                            BinaryPrimitives.ReadSingleLittleEndian(bytecode[offset..]),
                            BinaryPrimitives.ReadSingleLittleEndian(bytecode[(offset + 4)..]),
                            BinaryPrimitives.ReadSingleLittleEndian(bytecode[(offset + 8)..]),
                            BinaryPrimitives.ReadSingleLittleEndian(bytecode[(offset + 12)..])));
                        offset += 16;
                        break;

                    case OPCODE.STORE:
                        locals[bytecode[offset++]] = stack.Pop();
                        break;

                    case OPCODE.LOAD:
                        stack.Push(locals[bytecode[offset++]]);
                        break;

                    case OPCODE.OR:
                    case OPCODE.AND:
                    case OPCODE.EQUALS:
                    case OPCODE.NEQUALS:
                    case OPCODE.GT:
                    case OPCODE.GTE:
                    case OPCODE.LT:
                    case OPCODE.LTE:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();

                        stack.Push(new Vector4(
                            Compare(opcode, a.X, b.X),
                            Compare(opcode, a.Y, b.Y),
                            Compare(opcode, a.Z, b.Z),
                            Compare(opcode, a.W, b.W)));
                        break;
                    }

                    case OPCODE.NOT:
                    {
                        var a = stack.Pop();
                        stack.Push(new Vector4(a.X == 0f ? 1f : 0f, a.Y == 0f ? 1f : 0f, a.Z == 0f ? 1f : 0f, a.W == 0f ? 1f : 0f));
                        break;
                    }

                    case OPCODE.ADD:
                    case OPCODE.SUB:
                    case OPCODE.MUL:
                    case OPCODE.DIV:
                    case OPCODE.MODULO:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();

                        stack.Push(opcode switch
                        {
                            OPCODE.ADD => a + b,
                            OPCODE.SUB => a - b,
                            OPCODE.MUL => a * b,
                            OPCODE.DIV => a / b,
                            _ => new Vector4(a.X % b.X, a.Y % b.Y, a.Z % b.Z, a.W % b.W),
                        });
                        break;
                    }

                    case OPCODE.NEGATE:
                        stack.Push(-stack.Pop());
                        break;

                    case OPCODE.ATTRIBUTE:
                    {
                        isConstant = false;

                        if (!context.TryGetAttribute(BinaryPrimitives.ReadUInt32LittleEndian(bytecode[offset..]), out var attribute))
                        {
                            return false;
                        }

                        offset += 4;
                        stack.Push(attribute);
                        break;
                    }

                    case OPCODE.EXISTS:
                        isConstant = false;
                        stack.Push(context.TryGetAttribute(BinaryPrimitives.ReadUInt32LittleEndian(bytecode[offset..]), out _) ? Vector4.One : Vector4.Zero);
                        offset += 4;
                        break;

                    case OPCODE.MATERIAL_PARAM:
                    {
                        StringToken name = BinaryPrimitives.ReadUInt32LittleEndian(bytecode[offset..]);
                        offset += 4;

                        if (context.IsTextureParam(name))
                        {
                            stack.Push(new Vector4(float.NaN, float.NaN, float.NaN, BitConverter.UInt32BitsToSingle(name)));
                        }
                        else if (context.TryGetMaterialParam(name, out var param))
                        {
                            stack.Push(param);
                        }
                        else
                        {
                            return false;
                        }

                        break;
                    }

                    case OPCODE.FEATURE:
                        stack.Push(new Vector4(context.GetFeature(bytecode[offset++])));
                        break;

                    case OPCODE.SWIZZLE:
                    {
                        var swizzle = bytecode[offset++];
                        var value = stack.Pop();
                        stack.Push(new Vector4(value[swizzle & 3], value[(swizzle >> 2) & 3], value[(swizzle >> 4) & 3], value[(swizzle >> 6) & 3]));
                        break;
                    }

                    case OPCODE.SYSTEM_VALUE:
                        offset++;
                        stack.Push(Vector4.Zero);
                        break;

                    default:
                        return false;
                }
            }

            return false;
        }

        private static float Compare(OPCODE opcode, float a, float b)
        {
            var result = opcode switch
            {
                OPCODE.OR => a != 0f || b != 0f,
                OPCODE.AND => a != 0f && b != 0f,
                OPCODE.EQUALS => a == b,
                OPCODE.NEQUALS => a != b,
                OPCODE.GT => a > b,
                OPCODE.GTE => a >= b,
                OPCODE.LT => a < b,
                _ => a <= b,
            };

            return result ? 1f : 0f;
        }

        private static bool CallFunction(Function function, ref ValueStack stack, IVfxEvalContext context)
        {
            switch (function)
            {
                case Function.Sin: stack.Push(Vector4.Sin(stack.Pop())); break;
                case Function.Cos: stack.Push(Vector4.Cos(stack.Pop())); break;
                case Function.Tan: stack.Push(Map(stack.Pop(), MathF.Tan)); break;
                case Function.Frac: stack.Push(Map(stack.Pop(), static x => x - MathF.Floor(x))); break;
                case Function.Floor: stack.Push(Map(stack.Pop(), MathF.Floor)); break;
                case Function.Ceil: stack.Push(Map(stack.Pop(), MathF.Ceiling)); break;
                case Function.Saturate: stack.Push(Vector4.Clamp(stack.Pop(), Vector4.Zero, Vector4.One)); break;
                case Function.Clamp:
                {
                    var max = stack.Pop();
                    var min = stack.Pop();
                    stack.Push(Vector4.Clamp(stack.Pop(), min, max));
                    break;
                }
                case Function.Lerp:
                {
                    var t = stack.Pop();
                    var b = stack.Pop();
                    stack.Push(Vector4.Lerp(stack.Pop(), b, t));
                    break;
                }
                case Function.Dot4:
                {
                    var b = stack.Pop();
                    stack.Push(new Vector4(Vector4.Dot(stack.Pop(), b)));
                    break;
                }
                case Function.Dot3:
                {
                    var b = stack.Pop().AsVector3();
                    stack.Push(new Vector4(Vector3.Dot(stack.Pop().AsVector3(), b)));
                    break;
                }
                case Function.Dot2:
                {
                    var b = stack.Pop().AsVector2();
                    stack.Push(new Vector4(Vector2.Dot(stack.Pop().AsVector2(), b)));
                    break;
                }
                case Function.Log: stack.Push(Vector4.Log(stack.Pop())); break;
                case Function.Log2: stack.Push(Vector4.Log2(stack.Pop())); break;
                case Function.Log10: stack.Push(Map(stack.Pop(), MathF.Log10)); break;
                case Function.Exp: stack.Push(Vector4.Exp(stack.Pop())); break;
                case Function.Exp2: stack.Push(Map(stack.Pop(), static x => MathF.Pow(2f, x))); break;
                case Function.Sqrt: stack.Push(Vector4.SquareRoot(stack.Pop())); break;
                case Function.Rsqrt: stack.Push(Vector4.One / Vector4.SquareRoot(stack.Pop())); break;
                case Function.Sign: stack.Push(Map(stack.Pop(), static x => x > 0f ? 1f : x < 0f ? -1f : 0f)); break;
                case Function.Abs: stack.Push(Vector4.Abs(stack.Pop())); break;
                case Function.Pow:
                {
                    var b = stack.Pop();
                    var a = stack.Pop();
                    stack.Push(new Vector4(MathF.Pow(a.X, b.X), MathF.Pow(a.Y, b.Y), MathF.Pow(a.Z, b.Z), MathF.Pow(a.W, b.W)));
                    break;
                }
                case Function.Step:
                {
                    // First operand against second, unlike the HLSL function of this name
                    var b = stack.Pop();
                    var a = stack.Pop();
                    stack.Push(new Vector4(a.X >= b.X ? 1f : 0f, a.Y >= b.Y ? 1f : 0f, a.Z >= b.Z ? 1f : 0f, a.W >= b.W ? 1f : 0f));
                    break;
                }
                case Function.SmoothStep:
                {
                    var x = stack.Pop();
                    var max = stack.Pop();
                    var min = stack.Pop();
                    var t = Vector4.Clamp((x - min) / (max - min), Vector4.Zero, Vector4.One);
                    stack.Push(t * t * (new Vector4(3f) - 2f * t));
                    break;
                }
                case Function.Float4:
                {
                    var w = stack.Pop().X;
                    var z = stack.Pop().X;
                    var y = stack.Pop().X;
                    stack.Push(new Vector4(stack.Pop().X, y, z, w));
                    break;
                }
                case Function.Float3:
                {
                    var z = stack.Pop().X;
                    var y = stack.Pop().X;
                    stack.Push(new Vector4(stack.Pop().X, y, z, z));
                    break;
                }
                case Function.Float2:
                {
                    var y = stack.Pop().X;
                    stack.Push(new Vector4(stack.Pop().X, y, y, y));
                    break;
                }
                case Function.Time: stack.Push(new Vector4(context.Time)); break;
                case Function.Min:
                {
                    var b = stack.Pop();
                    stack.Push(Vector4.Min(stack.Pop(), b));
                    break;
                }
                case Function.Max:
                {
                    var b = stack.Pop();
                    stack.Push(Vector4.Max(stack.Pop(), b));
                    break;
                }
                case Function.SrgbLinearToGamma:
                {
                    var color = stack.Pop();
                    stack.Push(new Vector4(ColorSpace.SrgbLinearToGamma(color.AsVector3()), color.W));
                    break;
                }
                case Function.SrgbGammaToLinear:
                {
                    var color = stack.Pop();
                    stack.Push(new Vector4(ColorSpace.SrgbGammaToLinear(color.AsVector3()), color.W));
                    break;
                }
                case Function.Random:
                {
                    var max = stack.Pop().X;
                    var min = stack.Pop().X;
                    stack.Push(new Vector4(min + Random.Shared.NextSingle() * (max - min)));
                    break;
                }
                case Function.Normalize:
                {
                    var v = stack.Pop();
                    stack.Push(new Vector4(Vector3.Normalize(v.AsVector3()), v.W));
                    break;
                }
                case Function.Length: stack.Push(new Vector4(stack.Pop().AsVector3().Length())); break;
                case Function.Sqr:
                {
                    var v = stack.Pop();
                    stack.Push(v * v);
                    break;
                }
                case Function.TextureSize:
                {
                    var texture = stack.Pop();
                    stack.Push(IsTexture(texture) ? context.GetTextureSize(BitConverter.SingleToUInt32Bits(texture.W)) : Vector4.Zero);
                    break;
                }
                case Function.TextureAverageColor:
                {
                    var texture = stack.Pop();
                    stack.Push(IsTexture(texture) ? context.GetTextureAverageColor(BitConverter.SingleToUInt32Bits(texture.W)) : Vector4.Zero);
                    break;
                }

                // Expression matrices are column vector, the transpose of what System.Numerics builds
                case Function.MatrixIdentity: PushMatrix(ref stack, Matrix4x4.Identity); break;
                case Function.MatrixScale: PushMatrix(ref stack, Matrix4x4.CreateScale(stack.Pop().AsVector3())); break;
                case Function.MatrixTranslate: PushMatrix(ref stack, Matrix4x4.Transpose(Matrix4x4.CreateTranslation(stack.Pop().AsVector3()))); break;
                case Function.MatrixAxisAngle:
                {
                    var axisAngle = stack.Pop();
                    PushMatrix(ref stack, Matrix4x4.Transpose(Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axisAngle.AsVector3()), axisAngle.W)));
                    break;
                }
                case Function.MatrixAxisToAxis:
                {
                    var to = Vector3.Normalize(stack.Pop().AsVector3());
                    var from = Vector3.Normalize(stack.Pop().AsVector3());
                    PushMatrix(ref stack, Matrix4x4.Transpose(AxisToAxis(from, to)));
                    break;
                }
                case Function.MatrixMultiply:
                {
                    var b = PopMatrix(ref stack);
                    PushMatrix(ref stack, Matrix4x4.Multiply(PopMatrix(ref stack), b));
                    break;
                }
                case Function.MatrixColorCorrect: PushMatrix(ref stack, VfxEvalFunctions.MatrixColorCorrect2(stack.Pop().AsVector3(), new Vector3(0.5f))); break;
                case Function.MatrixColorCorrect2:
                {
                    var colorOffset = stack.Pop().AsVector3();
                    PushMatrix(ref stack, VfxEvalFunctions.MatrixColorCorrect2(stack.Pop().AsVector3(), colorOffset));
                    break;
                }
                case Function.MatrixColorTint: PushMatrix(ref stack, VfxEvalFunctions.MatrixColorTint2(stack.Pop().AsVector3(), 1f)); break;
                case Function.NormalizeSafe:
                {
                    var v = stack.Pop();
                    var xyz = v.AsVector3();
                    stack.Push(new Vector4(xyz.LengthSquared() > 0f ? Vector3.Normalize(xyz) : Vector3.Zero, v.W));
                    break;
                }
                case Function.Remap01ScaleOffset:
                {
                    var v = stack.Pop();
                    stack.Push(new Vector4(v.Y - v.X, v.X, v.Z, v.W));
                    break;
                }
                case Function.Radians: stack.Push(stack.Pop() * (MathF.PI / 180f)); break;
                case Function.Degrees: stack.Push(stack.Pop() * (180f / MathF.PI)); break;
                case Function.MatrixColorTint2:
                {
                    var strength = stack.Pop().X;
                    PushMatrix(ref stack, VfxEvalFunctions.MatrixColorTint2(stack.Pop().AsVector3(), strength));
                    break;
                }
                case Function.MatrixColorTint3:
                {
                    var mode = (int)stack.Pop().X;
                    var strength = stack.Pop().X;
                    PushMatrix(ref stack, VfxEvalFunctions.MatrixColorTint3(stack.Pop().AsVector3(), strength, mode));
                    break;
                }
                case Function.RemapVal:
                case Function.RemapValClamped:
                {
                    var outMax = stack.Pop();
                    var outMin = stack.Pop();
                    var inMax = stack.Pop();
                    var inMin = stack.Pop();
                    var remapped = outMin + (stack.Pop() - inMin) / (inMax - inMin) * (outMax - outMin);
                    stack.Push(function == Function.RemapValClamped ? Vector4.Clamp(remapped, Vector4.Min(outMin, outMax), Vector4.Max(outMin, outMax)) : remapped);
                    break;
                }

                // Rotation2d, Rotate2d, SinCos and anything newer
                default:
                    return false;
            }

            return true;
        }

        // Texture parameters push NaN with their name in the last component
        private static bool IsTexture(Vector4 value) => float.IsNaN(value.X) && float.IsNaN(value.Y) && float.IsNaN(value.Z);

        private static Vector4 Map(Vector4 v, Func<float, float> function)
            => new(function(v.X), function(v.Y), function(v.Z), function(v.W));

        private static Matrix4x4 AxisToAxis(Vector3 from, Vector3 to)
        {
            var dot = Vector3.Dot(from, to);

            if (dot >= 1f)
            {
                return Matrix4x4.Identity;
            }

            if (dot <= -1f)
            {
                var orthogonal = MathF.Abs(from.X) < 0.99f ? Vector3.UnitX : Vector3.UnitY;
                return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, orthogonal)), MathF.PI);
            }

            return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, to)), MathF.Acos(dot));
        }

        private static Matrix4x4 PopMatrix(ref ValueStack stack)
        {
            var row4 = stack.Pop();
            var row3 = stack.Pop();
            var row2 = stack.Pop();
            var row1 = stack.Pop();

            return new Matrix4x4(
                row1.X, row1.Y, row1.Z, row1.W,
                row2.X, row2.Y, row2.Z, row2.W,
                row3.X, row3.Y, row3.Z, row3.W,
                row4.X, row4.Y, row4.Z, row4.W);
        }

        private static void PushMatrix(ref ValueStack stack, in Matrix4x4 matrix)
        {
            stack.Push(new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14));
            stack.Push(new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24));
            stack.Push(new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34));
            stack.Push(new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44));
        }
    }
}
