using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class IKBody
{
    public float Mass { get; }
    public Vector4 VLocalMassCenter { get; }
    public Vector4 VRadius { get; }
    public float Resistance { get; }

    public IKBody(KVObject data)
    {
        Mass = data.GetFloatProperty("m_flMass");
        //VLocalMassCenter = m_vLocalMassCenter;
        //VRadius = m_vRadius;
        Resistance = data.GetFloatProperty("m_flResistance");
    }
}
