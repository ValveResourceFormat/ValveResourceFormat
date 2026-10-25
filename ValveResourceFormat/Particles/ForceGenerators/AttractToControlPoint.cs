using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Applies a gravitational pull force toward a control point, with configurable strength
/// and distance-based falloff power.
/// </summary>
/// <remarks>
/// "Pull Towards Control Point" in the particle editor. Negative force amounts repel particles. The falloff
/// power acts in quarter steps, and within <c>m_fMinimumDistance</c> the pull is the force amount times the
/// offset from the target, unnormalized and without falloff.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_AttractToControlPoint">C_OP_AttractToControlPoint</seealso>
class AttractToControlPoint : ParticleFunctionForceGenerator
{
    private readonly Vector3 componentScale = Vector3.One;
    private readonly INumberProvider forceAmount = new LiteralNumberProvider(100);
    private readonly INumberProvider minimumDistance = new LiteralNumberProvider(0);
    private readonly INumberProvider forceAmountMin = new LiteralNumberProvider(0);
    private readonly bool applyMinForce;
    private readonly float falloff = 2;
    private readonly ITransformProvider transformInput;

    public AttractToControlPoint(ParticleDefinitionParser parse) : base(parse)
    {
        componentScale = parse.Vector3("m_vecComponentScale", componentScale);
        forceAmount = parse.NumberProvider("m_fForceAmount", forceAmount);
        minimumDistance = parse.NumberProvider("m_fMinimumDistance", minimumDistance);
        forceAmountMin = parse.NumberProvider("m_fForceAmountMin", forceAmountMin);
        applyMinForce = parse.Boolean("m_bApplyMinForce", applyMinForce);
        falloff = parse.Float("m_fFalloffPower", falloff);
        transformInput = parse.TransformInput("m_TransformInput", new ControlPointTransformProvider(0, useOrientation: false));
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var transform = transformInput.NextTransform(particleSystemState);
        var target = transform.Translation;
        var scale = Vector3.TransformNormal(componentScale, transform) * strength;
        var current = particles.Current;
        var positions = current.ReadVectors(ParticleField.Position);
        var amounts = forceAmount.Fill(current, particleSystemState);
        var minAmounts = applyMinForce ? forceAmountMin.Fill(current, particleSystemState) : default;
        var minimumDistances = minimumDistance.Fill(current, particleSystemState);

        for (var i = 0; i < positions.Length; i++)
        {
            var amount = amounts[i];
            if (applyMinForce)
            {
                amount = MathF.Max(amount, minAmounts[i]);
            }

            var diff = positions[i] - target;
            var distance = diff.Length();

            if (distance < minimumDistances[i])
            {
                distance = 1f;
            }

            if (distance <= ParticleMath.FloatEpsilon)
            {
                continue;
            }

            var forceMagnitude = amount / distance * ParticleMath.PowQuarterSteps(distance, -falloff);
            forces[i] -= diff * forceMagnitude * scale;
        }
    }
}
