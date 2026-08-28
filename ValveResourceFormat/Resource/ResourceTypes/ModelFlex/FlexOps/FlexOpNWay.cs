namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that performs N-way blending between values.
    /// </summary>
    internal class FlexOpNWay : FlexOp
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
            var tController = context.PopIndex();
            var valueController = (int)MathF.Round(Data);

            var tCurrent = context.GetControllerValue(tController);
            var value = context.GetControllerValue(valueController);

            var t4 = context.Pop();
            var t3 = context.Pop();
            var t2 = context.Pop();
            var t1 = context.Pop();

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
