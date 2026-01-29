using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class BoneMaskNode : BoneMaskValueNode
{
    public GlobalSymbol BoneMaskID { get; }

    public BoneMaskNode(KVObject data) : base(data)
    {
        BoneMaskID = data.GetProperty<string>("m_boneMaskID");
    }
}
