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
    private readonly Vector3 ComponentScale = Vector3.One;
    private readonly INumberProvider ForceAmount = new LiteralNumberProvider(100);
    private readonly INumberProvider ForceAmountMin = new LiteralNumberProvider(0);
    private readonly bool ApplyMinForce;
    private readonly float Falloff = 2;
    private readonly ITransformProvider transformInput;

    public AttractToControlPoint(ParticleDefinitionParser parse) : base(parse)
    {
        ComponentScale = parse.Vector3("m_vecComponentScale", ComponentScale);
        ForceAmount = parse.NumberProvider("m_fForceAmount", ForceAmount);
        ForceAmountMin = parse.NumberProvider("m_fForceAmountMin", ForceAmountMin);
        ApplyMinForce = parse.Boolean("m_bApplyMinForce", ApplyMinForce);
        Falloff = parse.Float("m_fFalloffPower", Falloff);
        // The target is m_TransformInput (a transform input defaulting to control point 1); older content
        // stores it as the legacy m_nControlPointNumber field instead.
        var legacyControlPoint = parse.Data.ContainsKey("m_nControlPointNumber") ? parse.Int32("m_nControlPointNumber") : 1;
        transformInput = parse.TransformInput("m_TransformInput", new ControlPointTransformProvider(legacyControlPoint, false));
    }

    public override void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
    {
        var target = transformInput.NextTransform(particleSystemState).Translation;
        var scale = ComponentScale * strength;

        foreach (ref var particle in particles.Current)
        {
            var diff = target - particle.Position;
            var distance = diff.Length();
            if (distance < 1e-6f)
            {
                continue;
            }

            var amount = ForceAmount.NextNumber(ref particle, particleSystemState);
            if (ApplyMinForce)
            {
                amount = MathF.Max(amount, ForceAmountMin.NextNumber(ref particle, particleSystemState));
            }

            var forceMagnitude = amount / MathF.Pow(distance, Falloff);
            particle.ForceAccumulator += (diff / distance) * forceMagnitude * scale;
        }
    }
}
