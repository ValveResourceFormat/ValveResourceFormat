using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class Blend2DNode : PoseNode
{
    public short[] SourceNodeIndices { get; }
    public short InputParameterNodeIdx0 { get; }
    public short InputParameterNodeIdx1 { get; }
    public Vector2[] Values { get; }
    public byte[] Indices { get; }
    public byte[] HullIndices { get; }
    public bool AllowLooping { get; }

    public Blend2DNode(KVObject data) : base(data)
    {
        SourceNodeIndices = data.GetArray<short>("m_sourceNodeIndices");
        InputParameterNodeIdx0 = data.GetInt16Property("m_nInputParameterNodeIdx0");
        InputParameterNodeIdx1 = data.GetInt16Property("m_nInputParameterNodeIdx1");
        Values = data.GetArray<Vector2>("m_values");
        Indices = data.GetArray<byte>("m_indices");
        HullIndices = data.GetArray<byte>("m_hullIndices");
        AllowLooping = data.GetProperty<bool>("m_bAllowLooping");
    }
}
