using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A pose node that passes its child pose through.</summary>
[KV3Transfer]
partial class PassthroughNode : PoseNode
{
    /// <summary>The index of the child pose node.</summary>
    public short ChildNodeIdx { get; } = -1;
}
