namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pushes each particle with a force that blends from a starting to an ending vector over its age.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_TimeVaryingForce">C_OP_TimeVaryingForce</seealso>
class TimeVaryingForce : ParticleFunctionForceGenerator
{
    private readonly float startLerpTime;
    private readonly float endLerpTime = 10f;
    private readonly Vector3 startingForce;
    private readonly Vector3 endingForce;

    public TimeVaryingForce(ParticleDefinitionParser parse) : base(parse)
    {
        startLerpTime = parse.Float("m_flStartLerpTime", startLerpTime);
        endLerpTime = parse.Float("m_flEndLerpTime", endLerpTime);
        startingForce = parse.Vector3("m_StartingForce", startingForce);
        endingForce = parse.Vector3("m_EndingForce", endingForce);
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var ages = particles.Current.ReadAges();

        for (var i = 0; i < ages.Length; i++)
        {
            var age = ages[i];
            float t;

            if (endLerpTime != startLerpTime)
            {
                t = MathUtils.Saturate((age - startLerpTime) / (endLerpTime - startLerpTime));
            }
            else
            {
                t = age > endLerpTime ? 1f : 0f;
            }

            forces[i] += Vector3.Lerp(startingForce, endingForce, t) * strength;
        }
    }
}
