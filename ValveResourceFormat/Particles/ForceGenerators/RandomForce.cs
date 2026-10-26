namespace ValveResourceFormat.Particles.ForceGenerators;

/// <summary>
/// Generates a random force within the specified range. A separate force is drawn for every particle,
/// so the effect scatters rather than pushing the whole system one way. The forces come from the
/// thread generator, three draws per block of four particles.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RandomForce">C_OP_RandomForce</seealso>
class RandomForce : ParticleFunctionForceGenerator
{
    private readonly Vector3 min = Vector3.Zero;
    private readonly Vector3 max = Vector3.Zero;

    public RandomForce(ParticleDefinitionParser parse) : base(parse)
    {
        min = parse.Vector3("m_MinForce");
        max = parse.Vector3("m_MaxForce");
    }

    public override void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength)
    {
        var generator = particleSystemState.ThreadRandom;
        var low = min * strength;
        var range = (max * strength) - low;
        var count = particles.Current.Length;

        for (var blockStart = 0; blockStart < count; blockStart += 4)
        {
            var drawsX = generator.Next();
            var drawsY = generator.Next();
            var drawsZ = generator.Next();
            var blockEnd = Math.Min(blockStart + 4, count);

            for (var i = blockStart; i < blockEnd; i++)
            {
                var lane = i - blockStart;
                var draws = new Vector3(drawsX[lane], drawsY[lane], drawsZ[lane]);

                forces[i] += low + (range * draws);
            }
        }
    }
}
