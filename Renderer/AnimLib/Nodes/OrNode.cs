using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class OrNode : BoolValueNode
{
    public short[] ConditionNodeIndices { get; }

    public OrNode(KVObject data) : base(data)
    {
        ConditionNodeIndices = data.GetArray<short>("m_conditionNodeIndices");
    }
}
