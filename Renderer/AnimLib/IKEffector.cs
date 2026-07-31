using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class IKEffector
{
    public int BodyIndex { get; }
    public bool Enabled { get; }
    public Vector3 VTargetPosition { get; }
    public Quaternion QTargetOrientation { get; }
    public float Weight { get; }

    public IKEffector(KVObject data)
    {
        BodyIndex = data.GetInt32Property("m_nBodyIndex");
        Enabled = data.GetProperty<bool>("m_bEnabled");
        VTargetPosition = data.GetSubCollection("m_vTargetPosition").ToVector3();
        QTargetOrientation = data.GetSubCollection("m_qTargetOrientation").ToQuaternion();
        Weight = data.GetFloatProperty("m_flWeight");
    }
}
