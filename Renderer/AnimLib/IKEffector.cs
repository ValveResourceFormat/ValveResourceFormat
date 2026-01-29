using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class IKEffector
{
    public int BodyIndex { get; }
    public bool Enabled { get; }
    public Vector4 VTargetPosition { get; }
    public Quaternion QTargetOrientation { get; }
    public float Weight { get; }

    public IKEffector(KVObject data)
    {
        BodyIndex = data.GetInt32Property("m_nBodyIndex");
        Enabled = data.GetProperty<bool>("m_bEnabled");
        //VTargetPosition = m_vTargetPosition;
        //QTargetOrientation = m_qTargetOrientation;
        Weight = data.GetFloatProperty("m_flWeight");
    }
}
