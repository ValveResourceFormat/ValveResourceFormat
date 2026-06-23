using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class SnapWeaponNode : PassthroughNode
{
    public short FlashedAmountNodeIdx { get; }
    public short WeaponCategoryNodeIdx { get; }
    public short WeaponTypeNodeIdx { get; }

    public SnapWeaponNode(KVObject data) : base(data)
    {
        FlashedAmountNodeIdx = data.GetInt16Property("m_nFlashedAmountNodeIdx");
        WeaponCategoryNodeIdx = data.GetInt16Property("m_nWeaponCategoryNodeIdx");
        WeaponTypeNodeIdx = data.GetInt16Property("m_nWeaponTypeNodeIdx");
    }
}
