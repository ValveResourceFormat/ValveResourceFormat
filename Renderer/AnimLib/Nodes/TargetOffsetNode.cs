using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TargetOffsetNode : TargetValueNode
{
    public short InputValueNodeIdx { get; }
    public bool IsBoneSpaceOffset { get; }
    public Quaternion RotationOffset { get; }
    public Vector4 TranslationOffset { get; }

    public TargetOffsetNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        IsBoneSpaceOffset = data.GetProperty<bool>("m_bIsBoneSpaceOffset");
        //RotationOffset = m_rotationOffset;
        //TranslationOffset = m_translationOffset;
    }
}
