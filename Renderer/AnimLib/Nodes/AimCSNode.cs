using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class AimCSNode : PassthroughNode
{
    public short VerticalAngleNodeIdx { get; }
    public short HorizontalAngleNodeIdx { get; }
    public short WeaponCategoryNodeIdx { get; }
    public short WeaponTypeNodeIdx { get; }
    public short WeaponActionNodeIdx { get; }
    public short WeaponDropNodeIdx { get; }
    public short IsDefusingNodeIdx { get; }
    public short CrouchWeightNodeIdx { get; }
    public float HandIKBlendInTimeSeconds { get; }
    public float ActionBlendTimeSeconds { get; }
    public float PlantingBlendTimeSeconds { get; }

    public AimCSNode(KVObject data) : base(data)
    {
        VerticalAngleNodeIdx = data.GetInt16Property("m_nVerticalAngleNodeIdx");
        HorizontalAngleNodeIdx = data.GetInt16Property("m_nHorizontalAngleNodeIdx");
        WeaponCategoryNodeIdx = data.GetInt16Property("m_nWeaponCategoryNodeIdx");
        WeaponTypeNodeIdx = data.GetInt16Property("m_nWeaponTypeNodeIdx");
        WeaponActionNodeIdx = data.GetInt16Property("m_nWeaponActionNodeIdx");
        WeaponDropNodeIdx = data.GetInt16Property("m_nWeaponDropNodeIdx");
        IsDefusingNodeIdx = data.GetInt16Property("m_nIsDefusingNodeIdx");
        CrouchWeightNodeIdx = data.GetInt16Property("m_nCrouchWeightNodeIdx");
        HandIKBlendInTimeSeconds = data.GetFloatProperty("m_flHandIKBlendInTimeSeconds");
        ActionBlendTimeSeconds = data.GetFloatProperty("m_flActionBlendTimeSeconds");
        PlantingBlendTimeSeconds = data.GetFloatProperty("m_flPlantingBlendTimeSeconds");
    }
}
