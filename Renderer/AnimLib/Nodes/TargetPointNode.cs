using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TargetPointNode : VectorValueNode
{
    public short InputValueNodeIdx { get; }
    public bool IsWorldSpaceTarget { get; }

    public TargetPointNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        IsWorldSpaceTarget = data.GetProperty<bool>("m_bIsWorldSpaceTarget");
    }
}
