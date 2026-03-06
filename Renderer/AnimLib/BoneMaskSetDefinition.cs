using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class BoneMaskSetDefinition
{
    public GlobalSymbol ID { get; }
    public BoneWeightList PrimaryWeightList { get; }
    public BoneWeightList[] SecondaryWeightLists { get; }

    public BoneMaskSetDefinition(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        PrimaryWeightList = new(data.GetProperty<KVObject>("m_primaryWeightList"));
        SecondaryWeightLists = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_secondaryWeightLists"), kv => new BoneWeightList(kv))];
    }
}
