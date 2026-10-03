using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class LayerBlendNode
{
    [KV3Transfer]
    internal partial class LayerDefinitionType
    {
        public short InputNodeIdx { get; } = -1;
        public short WeightValueNodeIdx { get; } = -1;
        public short BoneMaskValueNodeIdx { get; } = -1;
        public short RootMotionWeightValueNodeIdx { get; } = -1;
        public bool IsSynchronized { get; }
        public bool IgnoreEvents { get; }
        public bool IsStateMachineLayer { get; }
        public PoseBlendMode BlendMode { get; }
    }
}
