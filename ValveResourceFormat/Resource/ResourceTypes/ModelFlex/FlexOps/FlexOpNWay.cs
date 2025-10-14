namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that performs N-way blending between values.
    /// </summary>
    public class FlexOpNWay : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpNWay"/> class.
        /// </summary>
        public FlexOpNWay(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Performs N-way blending based on controller values and threshold points.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var tController = BitConverter.SingleToInt32Bits(context.Stack.Pop());
            var valueController = (int)MathF.Round(Data);

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
