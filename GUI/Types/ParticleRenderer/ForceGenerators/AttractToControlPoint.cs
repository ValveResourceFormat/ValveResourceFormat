using GUI.Types.ParticleRenderer.Operators;

namespace GUI.Types.ParticleRenderer.ForceGenerators;

class AttractToControlPoint : ParticleFunctionOperator
{
    private readonly Vector3 ComponentScale = Vector3.One;
    private readonly INumberProvider ForceAmount = new LiteralNumberProvider(100);
    private readonly float Falloff = 2;
    private readonly int ControlPoint;

    public AttractToControlPoint(ParticleDefinitionParser parse) : base(parse)
    {
        ComponentScale = parse.Vector3("m_vecComponentScale", ComponentScale);
        ForceAmount = parse.NumberProvider("m_fForceAmount", ForceAmount);
        Falloff = parse.Float("m_fFalloffPower");
        ControlPoint = parse.Int32("m_nControlPointNumber");
    }

    public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
    {
        foreach (ref var particle in particles.Current)
        {
            var target = particleSystemState.GetControlPoint(ControlPoint).Position;
            var diff = target - particle.Position;
            var distance = diff.Length();
            var direction = Vector3.Normalize(diff);

            var strength = distance == 0.0f ? 1f : ForceAmount.NextNumber() / Math.Pow(distance, Falloff);
            var force = direction * (float)strength;

            particle.Velocity += force * ComponentScale * frameTime;
        }
    }
}
