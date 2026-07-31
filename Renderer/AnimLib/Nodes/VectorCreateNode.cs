using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VectorCreateNode : VectorValueNode
{
    public short InputVectorValueNodeIdx { get; }
    public short InputValueXNodeIdx { get; }
    public short InputValueYNodeIdx { get; }
    public short InputValueZNodeIdx { get; }

    public VectorCreateNode(KVObject data) : base(data)
    {
        InputVectorValueNodeIdx = data.GetInt16Property("m_inputVectorValueNodeIdx");
        InputValueXNodeIdx = data.GetInt16Property("m_inputValueXNodeIdx");
        InputValueYNodeIdx = data.GetInt16Property("m_inputValueYNodeIdx");
        InputValueZNodeIdx = data.GetInt16Property("m_inputValueZNodeIdx");
    }
}
