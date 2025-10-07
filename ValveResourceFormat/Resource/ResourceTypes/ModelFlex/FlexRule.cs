using ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps;

namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    /// <summary>
    /// Represents a flex rule that evaluates flex operations.
    /// </summary>
    public class FlexRule
    {
        /// <summary>
        /// Gets the flex ID.
        /// </summary>
        public int FlexID { get; }
        /// <summary>
        /// Gets the flex operations for this rule.
        /// </summary>
        public FlexOp[] FlexOps { get; }
        private readonly Stack<float> stack = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexRule"/> class.
        /// </summary>
        public FlexRule(int flexID, FlexOp[] flexOps)
        {
            if (flexOps.Length == 0)
            {
                throw new ArgumentException("Flex ops array cannot be empty");
            }

            FlexID = flexID;
            FlexOps = flexOps;
        }

        /// <summary>
        /// Evaluates the flex rule with the given controller values.
        /// </summary>
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
