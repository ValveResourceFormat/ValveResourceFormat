using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_SmartProp: evaluates another smart prop at the current state. Its variables take the values
    /// of same-named variables of this level.
    /// </summary>
    internal sealed class SmartPropReferenceElement(KVObject data, SmartPropParser parser) : SmartPropElement(data, parser)
    {
        private readonly SmartPropAttribute smartProp = Attr.String(data, "m_sSmartProp");
        private readonly SmartPropAttribute localEvaluationState = Attr.Bool(data, "m_bLocalEvaluationState", true);

        public override bool IsolatesState(SmartPropContext ctx) => localEvaluationState.EvaluateBool(ctx);

        public override void Evaluate(SmartPropContext ctx)
        {
            var name = smartProp.EvaluateString(ctx);

            if (name.Length > 0 && ctx.LoadDefinition(name) is { } definition)
            {
                ctx.EvaluateNestedSmartProp(definition);
            }
        }
    }
}
