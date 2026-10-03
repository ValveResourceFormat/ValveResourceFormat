using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FootstepEventIDNode : IDValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public BitFlags EventConditionRules { get; }
}
