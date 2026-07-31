using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class AndNode : BoolValueNode
{
    public short[] ConditionNodeIndices { get; }

    public AndNode(KVObject data) : base(data)
    {
        ConditionNodeIndices = data.GetArray<short>("m_conditionNodeIndices");
    }
}
