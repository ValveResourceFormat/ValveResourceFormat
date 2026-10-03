using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TargetOffsetNode : TargetValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public bool IsBoneSpaceOffset { get; } = true;
    public Quaternion RotationOffset { get; }
    public Vector3 TranslationOffset { get; }
}
