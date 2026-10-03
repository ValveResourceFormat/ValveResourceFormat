using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FootstepEventPercentageThroughNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public FootPhaseCondition PhaseCondition { get; }
    public BitFlags EventConditionRules { get; }
}
