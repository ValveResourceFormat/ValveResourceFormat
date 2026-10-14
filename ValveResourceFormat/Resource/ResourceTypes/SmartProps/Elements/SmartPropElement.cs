using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps.Criteria;
using ValveResourceFormat.ResourceTypes.SmartProps.Modifiers;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// A node of the smart prop element tree. Its modifiers run before <see cref="Evaluate"/>.
    /// </summary>
    internal abstract class SmartPropElement(KVObject data, SmartPropParser parser)
    {
        public int ElementId { get; } = parser.ElementId(data);
        public SmartPropAttribute Enabled { get; } = Attr.Bool(data, "m_bEnabled", true);
        public List<SmartPropModifier> Modifiers { get; } = SmartPropParser.ParseModifiers(data);
        public List<SmartPropCriteria> SelectionCriteria { get; } = SmartPropParser.ParseCriteria(data);

        /// <summary>Whether changes the element makes to the evaluation state are undone after it.</summary>
        public virtual bool IsolatesState(SmartPropContext ctx) => true;

        public abstract void Evaluate(SmartPropContext ctx);
    }

    /// <summary>
    /// An element that evaluates a list of children.
    /// </summary>
    internal abstract class SmartPropGroupElement(KVObject data, SmartPropParser parser) : SmartPropElement(data, parser)
    {
        public List<SmartPropElement> Children { get; } = parser.ParseElements(data, "m_Children");
    }

    /// <summary>CSmartPropElement_Group: evaluates every child in order.</summary>
    internal sealed class GroupElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        public override void Evaluate(SmartPropContext ctx) => ctx.EvaluateChildren(Children);
    }

    /// <summary>CSmartPropElement_ModifyState: only its modifiers run, and their changes persist for later siblings.</summary>
    internal sealed class ModifyStateElement(KVObject data, SmartPropParser parser) : SmartPropElement(data, parser)
    {
        public override bool IsolatesState(SmartPropContext ctx) => false;

        public override void Evaluate(SmartPropContext ctx)
        {
        }
    }
}
