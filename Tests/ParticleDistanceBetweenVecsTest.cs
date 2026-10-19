using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Operators;

namespace Tests;

public class ParticleDistanceBetweenVecsTest
{
    [Test]
    public async Task FactorySupportsDistanceBetweenVecs()
    {
        await Assert.That(ParticleSupportInfo.IsOperatorSupported("C_OP_DistanceBetweenVecs")).IsTrue();
    }

    [Test]
    public async Task DistanceBetweenVecsUsesUnitSpanForEqualBoundsThenAppliesStrength()
    {
        var data = FunctionData();
        data["m_nFieldOutput"] = new KVObject((int)ParticleField.Radius);
        data["m_vecPoint1"] = LiteralVector(0f, 0f, 0f);
        data["m_vecPoint2"] = LiteralVector(3.5f, 0f, 0f);
        data["m_flInputMin"] = LiteralFloat(3f);
        data["m_flInputMax"] = LiteralFloat(3f);
        data["m_flOutputMin"] = LiteralFloat(10f);
        data["m_flOutputMax"] = LiteralFloat(20f);
        data["m_nSetMethod"] = new KVObject("PARTICLE_SET_ADD_TO_CURRENT_VALUE");
        var particles = Collection(new Particle { Radius = 4f });

        new DistanceBetweenVecs(Parser(data)).Operate(particles, 0.1f, new ParticleSystemState(), 0.25f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(7.75f);
    }

    [Test]
    public async Task DistanceBetweenVecsDividesDistanceByFrameTime()
    {
        var data = FunctionData();
        data["m_nFieldOutput"] = new KVObject((int)ParticleField.Radius);
        data["m_vecPoint1"] = LiteralVector(0f, 0f, 0f);
        data["m_vecPoint2"] = LiteralVector(1f, 0f, 0f);
        data["m_flInputMin"] = LiteralFloat(0f);
        data["m_flInputMax"] = LiteralFloat(10f);
        data["m_flOutputMin"] = LiteralFloat(0f);
        data["m_flOutputMax"] = LiteralFloat(10f);
        data["m_bDeltaTime"] = new KVObject(true);
        var particles = Collection(new Particle { Radius = 4f });

        new DistanceBetweenVecs(Parser(data)).Operate(particles, 0.5f, new ParticleSystemState(), 1f);

        await Assert.That(particles.Current[0].Radius).IsEqualTo(2f);
    }

    private static ParticleDefinitionParser Parser(KVObject data) => new(data, NullLogger.Instance, 12);

    private static ParticleCollection Collection(params Particle[] values)
    {
        var particles = new ParticleCollection(new Particle(), values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            particles.Add();
            values[i].Index = i;
            particles.Current[i] = values[i];
            particles.Initial[i] = values[i];
        }

        return particles;
    }

    private static KVObject FunctionData() => KVObject.Collection();

    private static KVObject LiteralFloat(float value)
    {
        var input = KVObject.Collection();
        input["m_nType"] = new KVObject("PF_TYPE_LITERAL");
        input["m_flLiteralValue"] = new KVObject(value);
        return input;
    }

    private static KVObject LiteralVector(float x, float y, float z)
    {
        var input = KVObject.Collection();
        input["m_nType"] = new KVObject("PVEC_TYPE_LITERAL");
        input["m_vLiteralValue"] = Vector(x, y, z);
        return input;
    }

    private static KVObject Vector(float x, float y, float z)
        => KVObject.Array([new KVObject(x), new KVObject(y), new KVObject(z)]);
}
