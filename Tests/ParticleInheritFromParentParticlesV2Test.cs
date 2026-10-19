using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.ResourceTypes;
using Resource = ValveResourceFormat.Resource;

namespace Tests;

public class ParticleInheritFromParentParticlesV2Test
{
    [Test]
    public async Task FactorySupportsInheritFromParentParticlesV2()
    {
        await Assert.That(ParticleSupportInfo.IsOperatorSupported("C_OP_InheritFromParentParticlesV2")).IsTrue();
    }

    [Test]
    public async Task WalksTheParentRowsInOrderAndWrapsInBothDirections()
    {
        var particles = ChildCollection(4);

        Operator(Definition()).Operate(particles, 0.1f, State(ParentSystem(1f, 2f, 4f)), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(1f);
        await Assert.That(particles.Current[1].Radius).IsEqualTo(2f);
        await Assert.That(particles.Current[2].Radius).IsEqualTo(4f);
        await Assert.That(particles.Current[3].Radius).IsEqualTo(1f);
        await Assert.That(particles.Current[3].ParentParticleIndex).IsEqualTo(0);

        var data = Definition();
        data["m_bReverse"] = true;
        var reverseParticles = ChildCollection(4);

        Operator(data).Operate(reverseParticles, 0.1f, State(ParentSystem(1f, 2f, 4f)), 1f);

        await Assert.That(reverseParticles.Current[0].Radius).IsEqualTo(4f);
        await Assert.That(reverseParticles.Current[1].Radius).IsEqualTo(2f);
        await Assert.That(reverseParticles.Current[2].Radius).IsEqualTo(1f);
        await Assert.That(reverseParticles.Current[3].Radius).IsEqualTo(4f);
        await Assert.That(reverseParticles.Current[3].ParentParticleIndex).IsEqualTo(2);
    }

    [Test]
    public async Task InterpolatesTheScalarTargetFromTheCurrentValue()
    {
        var particles = ChildCollection(1);

        Operator(Definition()).Operate(particles, 0.1f, State(ParentSystem(2f)), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(2f);

        var quarter = Definition();
        quarter["m_flInterpolation"] = LiteralFloat(0.25f);
        var quarterParticles = ChildCollection(1);
        quarterParticles.Current[0].Radius = 8f;

        Operator(quarter).Operate(quarterParticles, 0.1f, State(ParentSystem(4f)), 1f);

        await Assert.That(quarterParticles.Current[0].Radius).IsEqualTo(7f);

        var doubleStep = Definition();
        doubleStep["m_flInterpolation"] = LiteralFloat(2f);
        var doubleParticles = ChildCollection(1);
        doubleParticles.Current[0].Radius = 8f;

        Operator(doubleStep).Operate(doubleParticles, 0.1f, State(ParentSystem(4f)), 1f);

        await Assert.That(doubleParticles.Current[0].Radius).IsEqualTo(4f);
    }

    [Test]
    public async Task AppliesTheVectorTargetWithoutSaturatingTheFraction()
    {
        var extrapolate = Definition();
        extrapolate["m_nFieldOutput"] = 0;
        extrapolate["m_flInterpolation"] = LiteralFloat(2f);
        var particles = ChildCollection(1);
        particles.Current[0].SetVector(ParticleField.Position, new Vector3(1f, 1f, 1f));
        var parent = ParentSystem(1f);
        parent.CurrentParticles[0].SetVector(ParticleField.Position, new Vector3(2f, 2f, 2f));

        Operator(extrapolate).Operate(particles, 0.1f, State(parent), 1f);

        await Assert.That(particles.Current[0].GetVector(ParticleField.Position)).IsEqualTo(new Vector3(3f, 3f, 3f));

        var clamp = Definition();
        clamp["m_nFieldOutput"] = 6;
        clamp["m_flScale"] = LiteralFloat(2f);
        var clampParticles = ChildCollection(1);
        var clampParent = ParentSystem(1f);
        clampParent.CurrentParticles[0].SetVector(ParticleField.Color, new Vector3(0.75f, 0.75f, 0.75f));

        Operator(clamp).Operate(clampParticles, 0.1f, State(clampParent), 1f);

        await Assert.That(clampParticles.Current[0].GetVector(ParticleField.Color)).IsEqualTo(Vector3.One);
    }

    [Test]
    public async Task SubSampleBlendsAdjacentParentsByTheAccumulatedIncrement()
    {
        var data = Definition();
        data["m_bSubSample"] = true;
        data["m_nIncrement"] = LiteralFloat(0.5f);
        var particles = ChildCollection(3);

        Operator(data).Operate(particles, 0.1f, State(ParentSystem(0f, 1f, 2f)), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(0f);
        await Assert.That(particles.Current[1].Radius).IsEqualTo(0.5f);
        await Assert.That(particles.Current[2].Radius).IsEqualTo(1f);

        var heldParticles = ChildCollection(3);

        Operator(data).Operate(heldParticles, 0.1f, State(ParentSystem(1f, 2f)), 1f);

        await Assert.That(heldParticles.Current[0].Radius).IsEqualTo(1f);
        await Assert.That(heldParticles.Current[1].ParentParticleIndex).IsEqualTo(0);
        await Assert.That(heldParticles.Current[2].ParentParticleIndex).IsEqualTo(1);
    }

    [Test]
    public async Task KillBehaviorFiresOnlyWhenTheParentSystemIsEmpty()
    {
        var data = Definition();
        data["m_nMissingParentBehavior"] = 0;
        var particles = ChildCollection(1);
        particles.Current[0].ParentParticleId = int.MinValue;

        Operator(data).Operate(particles, 0.1f, State(ParentSystem(2f)), 1f);

        await Assert.That(particles.Current[0].MarkedAsKilled).IsFalse();
        await Assert.That(particles.Current[0].Radius).IsEqualTo(2f);

        var emptyParticles = ChildCollection(2);

        Operator(data).Operate(emptyParticles, 0.1f, State(ParentSystem()), 1f);

        await Assert.That(emptyParticles.Current[0].MarkedAsKilled).IsTrue();
        await Assert.That(emptyParticles.Current[1].MarkedAsKilled).IsTrue();
    }

    [Test]
    public async Task UnlinkedChildrenResolveTheWalkRowAndStoreTheLinkage()
    {
        var particles = ChildCollection(1);
        particles.Current[0].ParentParticleId = int.MinValue;
        particles.Current[0].Radius = 7f;

        Operator(Definition()).Operate(particles, 0.1f, State(ParentSystem(2f)), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(2f);
        await Assert.That(particles.Current[0].ParentParticleId).IsEqualTo(0);
        await Assert.That(particles.Current[0].ParentParticleIndex).IsEqualTo(0);

        var clamped = Definition();
        clamped["m_nMissingParentBehavior"] = 2;
        clamped["m_bReverse"] = true;
        var clampedParticles = ChildCollection(1);
        clampedParticles.Current[0].ParentParticleId = int.MinValue;

        Operator(clamped).Operate(clampedParticles, 0.1f, State(ParentSystem(1f, 2f, 4f)), 1f);

        await Assert.That(clampedParticles.Current[0].Radius).IsEqualTo(4f);
        await Assert.That(clampedParticles.Current[0].ParentParticleIndex).IsEqualTo(2);
    }

    [Test]
    public async Task ParsesTheAuthoredEnumMemberNameForm()
    {
        var data = Definition();
        data["m_nMissingParentBehavior"] = new KVObject("MISSING_PARENT_KILL");
        var particles = ChildCollection(1);

        Operator(data).Operate(particles, 0.1f, State(ParentSystem()), 1f);

        await Assert.That(particles.Current[0].MarkedAsKilled).IsTrue();
    }

    [Test]
    public async Task SelectsTheParentRowFromTheStoredLinkageOrARandomDraw()
    {
        var particles = ChildCollection(1);
        particles.Current[0].ParentParticleId = 5;
        particles.Current[0].ParentParticleIndex = 2;

        Operator(Definition()).Operate(particles, 0.1f, State(ParentSystem(1f, 2f, 4f)), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(4f);

        var data = Definition();
        data["m_bRandomDistribution"] = true;
        var randomParticles = ChildCollection(2);

        Operator(data).Operate(randomParticles, 0.1f, State(ParentSystem(3f)), 1f);

        await Assert.That(randomParticles.Current[0].Radius).IsEqualTo(3f);
        await Assert.That(randomParticles.Current[1].Radius).IsEqualTo(3f);
    }

    [Test]
    public async Task NoParentSystemLeavesTheChildUntouched()
    {
        var particles = ChildCollection(1);
        particles.Current[0].Radius = 7f;

        Operator(Definition()).Operate(particles, 0.1f, new ParticleSystemState(), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(7f);
    }

    private static InheritFromParentParticlesV2 Operator(KVObject data)
        => new(new ParticleDefinitionParser(data, NullLogger.Instance, 12));

    private static KVObject Definition()
        => KVObject.Collection();

    private static KVObject LiteralFloat(float value)
    {
        var input = KVObject.Collection();
        input["m_nType"] = new KVObject("PF_TYPE_LITERAL");
        input["m_flLiteralValue"] = new KVObject(value);
        return input;
    }

    private static ParticleCollection ChildCollection(int count)
    {
        var particles = new ParticleCollection(
            new Particle { ParentParticleId = -1, ParentParticleIndex = -1 }, Math.Max(count, 4));
        for (var i = 0; i < count; i++)
        {
            particles.Add();
            particles.Current[i] = particles.Initial[i];
        }

        return particles;
    }

    private static ParticleSystemState State(ParticleSystemSimulation parent)
        => new(parent.RenderState);

    private static ParticleSystemSimulation ParentSystem(params float[] radii)
    {
        var simulation = new ParticleSystemSimulation(ParticleSystem.Create(KVObject.Collection()), new NullFileLoader());
        simulation.BeginPrewarm(radii.Length);
        for (var i = 0; i < radii.Length; i++)
        {
            simulation.CurrentParticles[i].Radius = radii[i];
            simulation.CurrentParticles[i].ParticleId = i;
        }

        return simulation;
    }

    private sealed class NullFileLoader : IFileLoader
    {
        public Resource LoadFile(string file) => null!;
        public Resource LoadFileCompiled(string file) => null!;
        public ShaderCollection LoadShader(string shaderName) => null!;
        public Stream GetFileStream(string file) => null!;
    }
}
