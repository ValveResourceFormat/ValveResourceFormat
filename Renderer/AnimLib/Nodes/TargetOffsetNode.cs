using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TargetOffsetNode : TargetValueNode
{
    public short InputValueNodeIdx { get; }
    public bool IsBoneSpaceOffset { get; }
    public Quaternion RotationOffset { get; }
    public Vector3 TranslationOffset { get; }

    public TargetOffsetNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        IsBoneSpaceOffset = data.GetProperty<bool>("m_bIsBoneSpaceOffset");
        RotationOffset = data.GetSubCollection("m_rotationOffset").ToQuaternion();
        TranslationOffset = data.GetSubCollection("m_translationOffset").ToVector3();
    }
}
