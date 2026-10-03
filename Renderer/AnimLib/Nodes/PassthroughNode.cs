using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A pose node that passes its child pose through.</summary>
partial class PassthroughNode : PoseNode
{
    /// <summary>The index of the child pose node.</summary>
    public short ChildNodeIdx { get; }

    /// <summary>Reads the node definition from resource data.</summary>
    public PassthroughNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
    }
}
