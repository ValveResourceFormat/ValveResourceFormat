#nullable disable

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public abstract class FlexOp
    {
        public float Data { get; private set; }
        public abstract void Run(in FlexRuleContext context);

        protected FlexOp(float data)
        {
            Data = data;
        }

        public static FlexOp Build(string opCode, int data)
        {
            var floatData = BitConverter.Int32BitsToSingle(data);
            var flexOp = opCode switch
            {
                "FLEX_OP_FETCH1" => new FlexOpFetch1(data),
                "FLEX_OP_CONST" => new FlexOpConst(floatData),
                "FLEX_OP_MAX" => new FlexOpMax(floatData),
                "FLEX_OP_MIN" => new FlexOpMin(floatData),
                "FLEX_OP_ADD" => new FlexOpAdd(floatData),
                "FLEX_OP_SUB" => new FlexOpSub(floatData),
                "FLEX_OP_MUL" => new FlexOpMul(floatData),
                "FLEX_OP_DIV" => new FlexOpDiv(floatData),
                "FLEX_OP_NWAY" => new FlexOpNWay(data),
                _ => (FlexOp)null,
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
