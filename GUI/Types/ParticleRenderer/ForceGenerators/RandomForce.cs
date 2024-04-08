using GUI.Types.ParticleRenderer.Operators;

namespace GUI.Types.ParticleRenderer.ForceGenerators;

class RandomForce : ParticleFunctionOperator
{
    private readonly Vector3 Min = Vector3.Zero;
    private readonly Vector3 Max = Vector3.Zero;


    public RandomForce(ParticleDefinitionParser parse) : base(parse)
    {
        Min = parse.Vector3("m_MinForce");
        Max = parse.Vector3("m_MaxForce");
    }

    public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
    {
        foreach (ref var particle in particles.Current)
        {
            var force = ParticleCollection.RandomBetweenPerComponent(Random.Shared.Next(), Min, Max);
            particle.Velocity += force * frameTime;
        }
    }
}
