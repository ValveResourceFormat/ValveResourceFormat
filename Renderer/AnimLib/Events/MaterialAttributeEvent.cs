using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class MaterialAttributeEvent : Event
{
    public string AttributeName { get; }
    public GlobalSymbol AttributeNameToken { get; }
    public Particles.Utils.PiecewiseCurve X { get; }
    public Particles.Utils.PiecewiseCurve Y { get; }
    public Particles.Utils.PiecewiseCurve Z { get; }
    public Particles.Utils.PiecewiseCurve W { get; }

    public MaterialAttributeEvent(KVObject data) : base(data)
    {
        AttributeName = data.GetProperty<string>("m_attributeName");
        AttributeNameToken = data.GetProperty<string>("m_attributeNameToken");
        X = new(data.GetProperty<KVObject>("m_x"), false);
        Y = new(data.GetProperty<KVObject>("m_y"), false);
        Z = new(data.GetProperty<KVObject>("m_z"), false);
        W = new(data.GetProperty<KVObject>("m_w"), false);
    }
}
