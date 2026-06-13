namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    /// <summary>
    /// Context for evaluating flex rules.
    /// </summary>
    public readonly struct FlexRuleContext
    {
        /// <summary>
        /// Gets the controller values.
        /// </summary>
        public float[] ControllerValues { get; }
        /// <summary>
        /// Gets the stack for flex operations.
        /// </summary>
        public Stack<float> Stack { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexRuleContext"/> struct.
        /// </summary>
        public FlexRuleContext(Stack<float> stack, float[] controllerValues)
        {
            ControllerValues = controllerValues;
            Stack = stack;
        }
    }
}
