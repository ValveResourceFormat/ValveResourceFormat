using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TargetSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; }
    public float OrientationScoreWeight { get; }
    public float PositionScoreWeight { get; }
    public short ParameterNodeIdx { get; }
    public bool IgnoreInvalidOptions { get; }
    public bool IsWorldSpaceTarget { get; }

    public TargetSelectorNode(KVObject data) : base(data)
    {
        OptionNodeIndices = data.GetArray<short>("m_optionNodeIndices");
        OrientationScoreWeight = data.GetFloatProperty("m_flOrientationScoreWeight");
        PositionScoreWeight = data.GetFloatProperty("m_flPositionScoreWeight");
        ParameterNodeIdx = data.GetInt16Property("m_parameterNodeIdx");
        IgnoreInvalidOptions = data.GetProperty<bool>("m_bIgnoreInvalidOptions");
        IsWorldSpaceTarget = data.GetProperty<bool>("m_bIsWorldSpaceTarget");
    }
}
