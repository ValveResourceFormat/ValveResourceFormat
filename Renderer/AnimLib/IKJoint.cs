using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class IKJoint
{
    public int ParentIndex { get; }
    public int BodyIndex { get; }
    public Transform XLocalFrame { get; }
    public float SwingLimit { get; }
    public float MinTwistLimit { get; }
    public float MaxTwistLimit { get; }
    public float Weight { get; }

    public IKJoint(KVObject data)
    {
        ParentIndex = data.GetInt32Property("m_nParentIndex");
        BodyIndex = data.GetInt32Property("m_nBodyIndex");
        XLocalFrame = new(data.GetProperty<KVObject>("m_xLocalFrame"));
        SwingLimit = data.GetFloatProperty("m_flSwingLimit");
        MinTwistLimit = data.GetFloatProperty("m_flMinTwistLimit");
        MaxTwistLimit = data.GetFloatProperty("m_flMaxTwistLimit");
        Weight = data.GetFloatProperty("m_flWeight");
    }
}
