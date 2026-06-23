using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class Blend2DNode : PoseNode
{
    public short[] SourceNodeIndices { get; }
    public short InputParameterNodeIdx0 { get; }
    public short InputParameterNodeIdx1 { get; }
    public Vector2[] Values { get; }
    public uint[] Indices { get; }
    public uint[] HullIndices { get; }
    public bool AllowLooping { get; }

    public Blend2DNode(KVObject data) : base(data)
    {
        SourceNodeIndices = data.GetArray<short>("m_sourceNodeIndices");
        InputParameterNodeIdx0 = data.GetInt16Property("m_nInputParameterNodeIdx0");
        InputParameterNodeIdx1 = data.GetInt16Property("m_nInputParameterNodeIdx1");
        Values = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_values"), v => new Vector2(v.GetFloatProperty("0"), v.GetFloatProperty("1")))];
        Indices = data.GetArray<uint>("m_indices");
        HullIndices = data.GetArray<uint>("m_hullIndices");
        AllowLooping = data.GetProperty<bool>("m_bAllowLooping");
    }
}
