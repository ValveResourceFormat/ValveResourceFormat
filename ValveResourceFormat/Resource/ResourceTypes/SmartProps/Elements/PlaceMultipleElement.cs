using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_PlaceMultiple: evaluates the children <c>m_nCount</c> times at the same transform, stopping
    /// before the first instance for which <c>m_Expression</c> is true.
    /// </summary>
    internal sealed class PlaceMultipleElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        private readonly SmartPropAttribute count = Attr.Int(data, "m_nCount", 1);
        private readonly SmartPropAttribute expression = Attr.String(data, "m_Expression");

        public override void Evaluate(SmartPropContext ctx)
        {
            var instances = count.EvaluateInt(ctx);
            var saved = ctx.SaveInstanceValues();

            for (var i = 0; i < instances; i++)
            {
                ctx.PushPath(i);
                ctx.InstanceCount = instances;
                ctx.InstanceIndex = i;

                var stop = expression.EvaluateString(ctx) is { Length: > 0 } text && ctx.EvaluateExpressionBool(text);

                if (!stop)
                {
                    ctx.EvaluateChildren(Children);
                }

                ctx.PopPath();
                ctx.RestoreInstanceValues(saved);

                if (stop)
                {
                    break;
                }
            }
        }
    }
}
