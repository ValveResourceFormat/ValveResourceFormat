using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A named bone mask with a primary weight list and optional per-skeleton lists.</summary>
public class BoneMaskSetDefinition
{
    /// <summary>The mask ID.</summary>
    public GlobalSymbol ID { get; }
    /// <summary>The weights for the primary skeleton.</summary>
    public BoneWeightList PrimaryWeightList { get; }
    /// <summary>The weights for secondary skeletons.</summary>
    public BoneWeightList[] SecondaryWeightLists { get; }

    /// <summary>Reads the definition from resource data.</summary>
    public BoneMaskSetDefinition(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        PrimaryWeightList = new(data.GetProperty<KVObject>("m_primaryWeightList"));
        SecondaryWeightLists = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_secondaryWeightLists") ?? [], kv => new BoneWeightList(kv))];
    }
}
