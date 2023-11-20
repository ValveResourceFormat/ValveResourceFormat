using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public abstract class FlexOp
    {
        public float Data { get; private set; }
        public abstract void Run(FlexRuleContext context);

        protected FlexOp(float data)
        {
            Data = data;
        }

        public static FlexOp Build(string opCode, int data)
        {
            float floatData = BitConverter.Int32BitsToSingle(data);
            switch (opCode)
            {
                case "FLEX_OP_FETCH1":
                    return new FlexOpFetch1(data);
                case "FLEX_OP_CONST":
                    return new FlexOpConst(floatData);
                case "FLEX_OP_MAX":
                    return new FlexOpMax(floatData);
                case "FLEX_OP_MIN":
                    return new FlexOpMin(floatData);
                case "FLEX_OP_ADD":
                    return new FlexOpAdd(floatData);
                case "FLEX_OP_SUB":
                    return new FlexOpSub(floatData);
                case "FLEX_OP_MUL":
                    return new FlexOpMul(floatData);
                case "FLEX_OP_DIV":
                    return new FlexOpDiv(floatData);
                default:
                    throw new ArgumentException($"Unknown flex opcode: {opCode}");
            }
        }
    }
}