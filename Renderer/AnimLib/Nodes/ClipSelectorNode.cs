using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; }
    public short[] ConditionNodeIndices { get; }

    public ClipSelectorNode(KVObject data) : base(data)
    {
        OptionNodeIndices = data.GetArray<short>("m_optionNodeIndices");
        ConditionNodeIndices = data.GetArray<short>("m_conditionNodeIndices");
    }
}
