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

        public static FlexOp Build(string opCode, float data)
        {
            switch (opCode)
            {
                case "FLEX_OP_FETCH1":
                    return new FlexOpFetch1(data);
                case "FLEX_OP_CONST":
                    return new FlexOpConst(data);
                case "FLEX_OP_MAX":
                    return new FlexOpMax(data);
                case "FLEX_OP_MIN":
                    return new FlexOpMin(data);
                case "FLEX_OP_ADD":
                    return new FlexOpAdd(data);
                case "FLEX_OP_SUB":
                    return new FlexOpSub(data);
                case "FLEX_OP_MUL":
                    return new FlexOpMul(data);
                case "FLEX_OP_DIV":
                    return new FlexOpDiv(data);
                default:
                    throw new ArgumentException($"Unknown flex opcode: {opCode}");
            }
        }
    }
}
