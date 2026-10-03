using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class LayerBlendNode : PoseNode
{
    public short BaseNodeIdx { get; } = -1;
    public bool OnlySampleBaseRootMotion { get; } = true;
    public LayerBlendNode.LayerDefinitionType[] LayerDefinition { get; } = [];
}
