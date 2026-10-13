using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Animation;

[KV3Transfer]
public partial class GraphDefinition
{
    public StringToken VariationID { get; }
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public string[] SupportedSecondarySkeletons { get; } = [];
    public short[] PersistentNodeIndices { get; } = [];
    public short RootNodeIdx { get; } = -1;
    public StringToken[] ControlParameterIDs { get; } = [];
    public StringToken[] VirtualParameterIDs { get; } = [];
    public short[] VirtualParameterNodeIndices { get; } = [];
    public GraphDefinition.ReferencedGraphSlot[] ReferencedGraphSlots { get; } = [];
    public GraphDefinition.ExternalGraphSlot[] ExternalGraphSlots { get; } = [];
    public GraphDefinition.ExternalPoseSlot[] ExternalPoseSlots { get; } = [];
    public string[] NodePaths { get; } = [];
    public string[] Resources { get; } = [];
}

public partial class GraphDefinition
{
    [KV3Transfer]
    public partial class ExternalGraphSlot
    {
        public short NodeIdx { get; } = -1;
        public StringToken SlotID { get; }
    }
}

public partial class GraphDefinition
{
    [KV3Transfer]
    public partial class ExternalPoseSlot
    {
        public short NodeIdx { get; } = -1;
        public StringToken SlotID { get; }
    }
}

public partial class GraphDefinition
{
    [KV3Transfer]
    public partial class ReferencedGraphSlot
    {
        public short NodeIdx { get; } = -1;
        [KVProperty("m_dataSlotIdx")]
        public short DataSlotIdx { get; } = -1;
    }
}
