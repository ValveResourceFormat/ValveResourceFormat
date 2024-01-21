using ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps;

namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    public class FlexRule
    {
        public int FlexID { get; }
        public FlexOp[] FlexOps { get; }
        private readonly Stack<float> stack = new();

        public FlexRule(int flexID, FlexOp[] flexOps)
        {
            if (flexOps.Length == 0)
            {
                throw new ArgumentException("Flex ops array cannot be empty");
            }

            FlexID = flexID;
            FlexOps = flexOps;
        }

        public float Evaluate(float[] flexControllerValues)
        {
            var context = new FlexRuleContext(stack, flexControllerValues);

            foreach (var item in FlexOps)
            {
                item.Run(context);
            }

            if (stack.Count != 1)
            {
                throw new InvalidOperationException($"FlexRule stack had {stack.Count} values after evaluation");
            }

            return context.Stack.Pop();
        }
    }
}
