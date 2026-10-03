using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class GraphDefinition
{
    public GlobalSymbol VariationID { get; }
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public string[] SupportedSecondarySkeletons { get; } = [];
    public short[] PersistentNodeIndices { get; } = [];
    public short RootNodeIdx { get; } = -1;
    public GlobalSymbol[] ControlParameterIDs { get; } = [];
    public GlobalSymbol[] VirtualParameterIDs { get; } = [];
    public short[] VirtualParameterNodeIndices { get; } = [];
    public GraphDefinition__ReferencedGraphSlot[] ReferencedGraphSlots { get; } = [];
    public GraphDefinition__ExternalGraphSlot[] ExternalGraphSlots { get; } = [];
    public GraphDefinition__ExternalPoseSlot[] ExternalPoseSlots { get; } = [];
    public string[] NodePaths { get; } = [];
    public string[] Resources { get; } = [];
}
