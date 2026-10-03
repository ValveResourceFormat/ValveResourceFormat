using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class LayerBlendNode : PoseNode
{
    public short BaseNodeIdx { get; }
    public bool OnlySampleBaseRootMotion { get; }
    public LayerBlendNode__LayerDefinition[] LayerDefinition { get; }

    public LayerBlendNode(KVObject data) : base(data)
    {
        BaseNodeIdx = data.GetInt16Property("m_nBaseNodeIdx");
        OnlySampleBaseRootMotion = data.GetProperty<bool>("m_bOnlySampleBaseRootMotion");
        LayerDefinition = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_layerDefinition"), kv => new LayerBlendNode__LayerDefinition(kv))];
    }
}
