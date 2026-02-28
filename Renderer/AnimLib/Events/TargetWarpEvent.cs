using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class TargetWarpEvent : Event
{
    public TargetWarpRule Rule { get; }
    public TargetWarpAlgorithm Algorithm { get; }

    public TargetWarpEvent(KVObject data) : base(data)
    {
        Rule = data.GetEnumValue<TargetWarpRule>("m_rule");
        Algorithm = data.GetEnumValue<TargetWarpAlgorithm>("m_algorithm");
    }
}
