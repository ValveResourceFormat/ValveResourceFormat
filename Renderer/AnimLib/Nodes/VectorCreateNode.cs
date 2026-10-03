using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class VectorCreateNode : VectorValueNode
{
    [KVProperty("m_inputVectorValueNodeIdx")]
    public short InputVectorValueNodeIdx { get; } = -1;
    [KVProperty("m_inputValueXNodeIdx")]
    public short InputValueXNodeIdx { get; } = -1;
    [KVProperty("m_inputValueYNodeIdx")]
    public short InputValueYNodeIdx { get; } = -1;
    [KVProperty("m_inputValueZNodeIdx")]
    public short InputValueZNodeIdx { get; } = -1;
}
