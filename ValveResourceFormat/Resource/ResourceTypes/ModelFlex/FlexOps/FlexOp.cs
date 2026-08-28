using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Base class for flex operations.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CFlexOp">CFlexOp</seealso>
    public abstract class FlexOp
    {
        /// <summary>
        /// Gets the data associated with this operation.
        /// </summary>
        public float Data { get; private set; }
        /// <summary>
        /// Executes the flex operation.
        /// </summary>
        public abstract void Run(in FlexRuleContext context);

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOp"/> class.
        /// </summary>
        protected FlexOp(float data)
        {
            Data = data;
        }

        /// <summary>
        /// Parses the <see cref="FlexOpCode"/> a morph set stores for a flex op. Older morph sets store
        /// the ordinal, newer ones the enum name.
        /// </summary>
        public static FlexOpCode? ParseOpCode(KVObject opCode)
        {
            if (opCode.ValueType is KVValueType.UInt32 or KVValueType.Int32 or KVValueType.UInt64 or KVValueType.Int64)
            {
                return (FlexOpCode)(int)opCode;
            }

            if (opCode.ValueType == KVValueType.String)
            {
                return (string)opCode switch
                {
                    "FLEX_OP_CONST" => FlexOpCode.Const,
                    "FLEX_OP_FETCH1" => FlexOpCode.Fetch1,
                    "FLEX_OP_FETCH2" => FlexOpCode.Fetch2,
                    "FLEX_OP_ADD" => FlexOpCode.Add,
                    "FLEX_OP_SUB" => FlexOpCode.Sub,
                    "FLEX_OP_MUL" => FlexOpCode.Mul,
                    "FLEX_OP_DIV" => FlexOpCode.Div,
                    "FLEX_OP_NEG" => FlexOpCode.Neg,
                    "FLEX_OP_EXP" => FlexOpCode.Exp,
                    "FLEX_OP_OPEN" => FlexOpCode.Open,
                    "FLEX_OP_CLOSE" => FlexOpCode.Close,
                    "FLEX_OP_COMMA" => FlexOpCode.Comma,
                    "FLEX_OP_MAX" => FlexOpCode.Max,
                    "FLEX_OP_MIN" => FlexOpCode.Min,
                    "FLEX_OP_2WAY_0" => FlexOpCode.TwoWay0,
                    "FLEX_OP_2WAY_1" => FlexOpCode.TwoWay1,
                    "FLEX_OP_NWAY" => FlexOpCode.NWay,
                    "FLEX_OP_COMBO" => FlexOpCode.Combo,
                    "FLEX_OP_DOMINATE" => FlexOpCode.Dominate,
                    "FLEX_OP_DME_LOWER_EYELID" => FlexOpCode.DmeLowerEyelid,
                    "FLEX_OP_DME_UPPER_EYELID" => FlexOpCode.DmeUpperEyelid,
                    "FLEX_OP_SQRT" => FlexOpCode.Sqrt,
                    "FLEX_OP_REMAPVALCLAMPED" => FlexOpCode.RemapValClamped,
                    "FLEX_OP_SIN" => FlexOpCode.Sin,
                    "FLEX_OP_COS" => FlexOpCode.Cos,
                    "FLEX_OP_ABS" => FlexOpCode.Abs,
                    _ => null,
                };
            }

            return null;
        }

        /// <summary>
        /// Builds a flex operation from an opcode and data.
        /// </summary>
        public static FlexOp? Build(FlexOpCode? opCode, int data)
        {
            var floatData = BitConverter.Int32BitsToSingle(data);
            var flexOp = opCode switch
            {
                FlexOpCode.Fetch1 => new FlexOpFetch1(data),
                FlexOpCode.Const => new FlexOpConst(floatData),
                FlexOpCode.Max => new FlexOpMax(floatData),
                FlexOpCode.Min => new FlexOpMin(floatData),
                FlexOpCode.Add => new FlexOpAdd(floatData),
                FlexOpCode.Sub => new FlexOpSub(floatData),
                FlexOpCode.Mul => new FlexOpMul(floatData),
                FlexOpCode.Div => new FlexOpDiv(floatData),
                FlexOpCode.NWay => new FlexOpNWay(data),
                FlexOpCode.Fetch2 => new FlexOpFetch2(data),
                FlexOpCode.Neg => new FlexOpNeg(floatData),
                FlexOpCode.TwoWay0 => new FlexOpTwoWay0(data),
                FlexOpCode.TwoWay1 => new FlexOpTwoWay1(data),
                FlexOpCode.Combo => new FlexOpCombo(data),
                FlexOpCode.Dominate => new FlexOpDominate(data),
                FlexOpCode.DmeLowerEyelid => new FlexOpDmeLowerEyelid(data),
                FlexOpCode.DmeUpperEyelid => new FlexOpDmeUpperEyelid(data),
                FlexOpCode.Sqrt => new FlexOpSqrt(floatData),
                FlexOpCode.RemapValClamped => new FlexOpRemapValClamped(floatData),
                FlexOpCode.Sin => new FlexOpSin(floatData),
                FlexOpCode.Cos => new FlexOpCos(floatData),
                FlexOpCode.Abs => new FlexOpAbs(floatData),

                // The engine switch has no case for these, so they never reach a compiled op stream.
                FlexOpCode.Exp or FlexOpCode.Open or FlexOpCode.Close or FlexOpCode.Comma => new FlexOpNop(floatData),
                _ => (FlexOp?)null,
            };

#if DEBUG
            if (flexOp is null)
            {
                Console.WriteLine($"Unknown flex opcode: {opCode}");
            }
#endif

            return flexOp;
        }
    }
}
