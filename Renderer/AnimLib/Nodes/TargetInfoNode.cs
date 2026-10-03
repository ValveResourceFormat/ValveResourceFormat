using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TargetInfoNode : FloatValueNode
{
    public short InputValueNodeIdx { get; }
    public TargetInfoNode__Info InfoType { get; }
    public bool IsWorldSpaceTarget { get; }

    public TargetInfoNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        InfoType = data.GetEnumValue<TargetInfoNode__Info>("m_infoType");
        IsWorldSpaceTarget = data.GetProperty<bool>("m_bIsWorldSpaceTarget");
    }
}
