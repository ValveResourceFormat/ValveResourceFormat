using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ParameterizedBlendNode : PoseNode
{
    public short[] SourceNodeIndices { get; }
    public short InputParameterValueNodeIdx { get; }
    public bool AllowLooping { get; }

    public ParameterizedBlendNode(KVObject data) : base(data)
    {
        SourceNodeIndices = data.GetArray<short>("m_sourceNodeIndices");
        InputParameterValueNodeIdx = data.GetInt16Property("m_nInputParameterValueNodeIdx");
        AllowLooping = data.GetProperty<bool>("m_bAllowLooping");
    }
}
