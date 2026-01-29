using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class SelectorNode : PoseNode
{
    public short[] OptionNodeIndices { get; }
    public short[] ConditionNodeIndices { get; }

    public SelectorNode(KVObject data) : base(data)
    {
        OptionNodeIndices = data.GetArray<short>("m_optionNodeIndices");
        ConditionNodeIndices = data.GetArray<short>("m_conditionNodeIndices");
    }
}
