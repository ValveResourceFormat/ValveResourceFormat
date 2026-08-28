using ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps;

namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    /// <summary>
    /// Represents a flex rule that evaluates flex operations.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CFlexRule">CFlexRule</seealso>
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
        /// Gets the number of flex value slots needed to evaluate a set of rules, one more than the
        /// highest flex ID among them.
        /// </summary>
        public static int GetFlexValueCount(IReadOnlyCollection<FlexRule> rules)
        {
            var count = 0;

            foreach (var rule in rules)
            {
                count = Math.Max(count, rule.FlexID + 1);
            }

            return count;
        }

        /// <summary>
        /// Evaluates the flex rule with the given controller values.
        /// </summary>
        public float Evaluate(float[] flexControllerValues)
        {
            return Evaluate(flexControllerValues, null, null);
        }

        /// <summary>
        /// Evaluates the flex rule, giving the operations that need them the controller ranges and the
        /// values of the flexes evaluated so far.
        /// </summary>
        public float Evaluate(float[] flexControllerValues, FlexController[]? controllers, float[]? flexValues)
        {
            var context = new FlexRuleContext(stack, flexControllerValues, controllers, flexValues);

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
