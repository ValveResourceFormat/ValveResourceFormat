namespace ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps
{
    /// <summary>
    /// Flex operation that drives a lower eyelid from the blink and gaze controllers.
    /// </summary>
    internal class FlexOpDmeLowerEyelid : FlexOp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FlexOpDmeLowerEyelid"/> class.
        /// </summary>
        public FlexOpDmeLowerEyelid(float data) : base(data) { }

        /// <inheritdoc/>
        /// <remarks>
        /// Pops the gaze, blink and lid controller indices and pushes the lower lid weight.
        /// </remarks>
        public override void Run(in FlexRuleContext context)
        {
            var closeLidV = context.GetRemappedControllerValue((int)MathF.Round(Data), 0f, 1f);
            var closeLid = context.GetRemappedControllerValue(context.PopIndex(), 0f, 1f);
            var blinkIndex = context.PopIndex();
            var eyeUpDownIndex = context.PopIndex();

            var blink = blinkIndex >= 0 ? context.GetRemappedControllerValue(blinkIndex, 0f, 1f) : 0f;
            var eyeUpDown = eyeUpDownIndex >= 0 ? context.GetRemappedControllerValue(eyeUpDownIndex, -1f, 1f) : 0f;

            var closed = MathF.Max(blink, closeLid);

            context.Stack.Push(closed * (1f - closeLidV) * (eyeUpDown > 0f ? 1f - eyeUpDown : 1f));
        }
    }
}
