using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles.ForceGenerators;

/// <summary>
/// Applies a gravitational pull force toward a control point, with configurable strength
/// and distance-based falloff power.
/// </summary>
/// <remarks>
/// "Pull Towards Control Point" in the particle editor. Can also be used to repel particles
/// by using negative values for the amount of force.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_AttractToControlPoint">C_OP_AttractToControlPoint</seealso>
class AttractToControlPoint : ParticleFunctionForceGenerator
{
    private readonly Vector3 componentScale = Vector3.One;
    private readonly INumberProvider forceAmount = new LiteralNumberProvider(100);
    private readonly INumberProvider forceAmountMin = new LiteralNumberProvider(0);
    private readonly bool applyMinForce;
    private readonly float falloff = 2;
    private readonly ITransformProvider transformInput;

    public AttractToControlPoint(ParticleDefinitionParser parse) : base(parse)
    {
        componentScale = parse.Vector3("m_vecComponentScale", componentScale);
        forceAmount = parse.NumberProvider("m_fForceAmount", forceAmount);
        forceAmountMin = parse.NumberProvider("m_fForceAmountMin", forceAmountMin);
        applyMinForce = parse.Boolean("m_bApplyMinForce", applyMinForce);
        falloff = parse.Float("m_fFalloffPower", falloff);
        transformInput = parse.TransformInput("m_TransformInput", new ControlPointTransformProvider(1, false));
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        var target = transformInput.NextTransform(particleSystemState).Translation;
        var scale = componentScale * strength;

        foreach (ref var particle in particles.Current)
        {
            var diff = target - particle.Position;
            var distance = diff.Length();
            if (distance < Epsilon.Length)
            {
                continue;
            }

            var amount = forceAmount.NextNumber(ref particle, particleSystemState);
            if (applyMinForce)
            {
                amount = MathF.Max(amount, forceAmountMin.NextNumber(ref particle, particleSystemState));
            }

            var forceMagnitude = amount / MathF.Pow(distance, falloff);
            particle.ForceAccumulator += (diff / distance) * forceMagnitude * scale;
        }
    }
}
