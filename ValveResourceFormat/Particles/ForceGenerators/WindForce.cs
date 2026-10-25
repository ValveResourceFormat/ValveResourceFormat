namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Pushes particles like sails by a constant wind <c>m_vForce</c>, scaled by how squarely each
/// particle's normal faces it. The scale is the unnormalized dot product, so for a unit normal the
/// push grows with the square of the wind strength.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_WindForce">C_OP_WindForce</seealso>
class WindForce : ParticleFunctionForceGenerator
{
    private readonly Vector3 force;

    public WindForce(ParticleDefinitionParser parse) : base(parse)
    {
        force = parse.Vector3("m_vForce", force);
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var wind = force * strength;
        var normals = particles.Current.ReadVectors(ParticleField.Normal);

        for (var i = 0; i < normals.Length; i++)
        {
            forces[i] += wind * MathF.Abs(Vector3.Dot(normals[i], wind));
        }
    }
}
