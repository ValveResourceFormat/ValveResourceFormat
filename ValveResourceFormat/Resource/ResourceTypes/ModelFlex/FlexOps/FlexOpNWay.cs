using System;

namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    public class FlexOpNWay : FlexOp
    {
        public FlexOpNWay(float data) : base(data) { }

        public override void Run(FlexRuleContext context)
        {
            var tController = BitConverter.SingleToInt32Bits(context.Stack.Pop());
            var valueController = (int)Math.Round(Data);

            var tCurrent = context.ControllerValues[tController];
            var value = context.ControllerValues[valueController];

            var t4 = context.Stack.Pop();
            var t3 = context.Stack.Pop();
            var t2 = context.Stack.Pop();
            var t1 = context.Stack.Pop();

            float outValue;
            if (tCurrent < t1)
            {
                outValue = 0f;
            }
            else if (tCurrent < t2)
            {
                outValue = float.Lerp(0, value, (tCurrent - t1) / (t2 - t1));
            }
            else if (tCurrent < t3)
            {
                outValue = value;
            }
            else if (tCurrent < t4)
            {
                outValue = float.Lerp(value, 0, (tCurrent - t3) / (t4 - t3));
            }
            else
            {
                outValue = 0f;
            }

            context.Stack.Push(outValue);
        }
    }
}
