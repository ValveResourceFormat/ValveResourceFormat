using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class Blend1DNode : ParameterizedBlendNode
{
    public ParameterizedBlendNode__Parameterization Parameterization { get; }

    public Blend1DNode(KVObject data) : base(data)
    {
        Parameterization = new(data.GetProperty<KVObject>("m_parameterization"));
    }
}
