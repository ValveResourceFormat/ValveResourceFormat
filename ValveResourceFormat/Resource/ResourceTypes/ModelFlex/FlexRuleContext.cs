namespace ValveResourceFormat.ResourceTypes.ModelFlex
{
    public readonly struct FlexRuleContext
    {
        public float[] ControllerValues { get; }
        public Stack<float> Stack { get; }

        public FlexRuleContext(Stack<float> stack, float[] controllerValues)
        {
            ControllerValues = controllerValues;
            Stack = stack;
        }
    }
}
