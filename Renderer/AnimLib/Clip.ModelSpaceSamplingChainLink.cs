using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class Clip
{
    [KV3Transfer]
    internal partial class ModelSpaceSamplingChainLink
    {
        public int BoneIdx { get; } = -1;
        public int ParentBoneIdx { get; } = -1;
        public int ParentChainLinkIdx { get; } = -1;
    }
}
