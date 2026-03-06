using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class IKBody
{
    public float Mass { get; }
    public Vector3 VLocalMassCenter { get; }
    public Vector3 VRadius { get; }
    public float Resistance { get; }

    public IKBody(KVObject data)
    {
        Mass = data.GetFloatProperty("m_flMass");
        VLocalMassCenter = data.GetSubCollection("m_vLocalMassCenter").ToVector3();
        VRadius = data.GetSubCollection("m_vRadius").ToVector3();
        Resistance = data.GetFloatProperty("m_flResistance");
    }
}
