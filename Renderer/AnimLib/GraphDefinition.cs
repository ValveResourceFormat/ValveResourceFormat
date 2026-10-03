using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class GraphDefinition
{
    public GlobalSymbol VariationID { get; }
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public short[] PersistentNodeIndices { get; }
    public short RootNodeIdx { get; }
    public GlobalSymbol[] ControlParameterIDs { get; }
    public GlobalSymbol[] VirtualParameterIDs { get; }
    public short[] VirtualParameterNodeIndices { get; }
    public GraphDefinition__ReferencedGraphSlot[] ReferencedGraphSlots { get; }
    public GraphDefinition__ExternalGraphSlot[] ExternalGraphSlots { get; }
    public GraphDefinition__ExternalPoseSlot[] ExternalPoseSlots { get; }
    public string[] NodePaths { get; }
    public string[] Resources { get; }

    public GraphDefinition(KVObject data)
    {
        VariationID = data.GetProperty<string>("m_variationID");
        Skeleton = data.GetProperty<string>("m_skeleton");
        PersistentNodeIndices = data.GetArray<short>("m_persistentNodeIndices");
        RootNodeIdx = data.GetInt16Property("m_nRootNodeIdx");
        ControlParameterIDs = data.GetSymbolArray("m_controlParameterIDs");
        VirtualParameterIDs = data.GetSymbolArray("m_virtualParameterIDs");
        VirtualParameterNodeIndices = data.GetArray<short>("m_virtualParameterNodeIndices");
        ReferencedGraphSlots = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_referencedGraphSlots"), kv => new GraphDefinition__ReferencedGraphSlot(kv))];
        ExternalGraphSlots = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_externalGraphSlots"), kv => new GraphDefinition__ExternalGraphSlot(kv))];
        ExternalPoseSlots = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_externalPoseSlots"), kv => new GraphDefinition__ExternalPoseSlot(kv))];
        NodePaths = data.GetArray<string>("m_nodePaths");
        Resources = data.GetArray<string>("m_resources");
    }
}
