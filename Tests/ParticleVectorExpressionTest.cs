using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using InitVectorExpression = ValveResourceFormat.Particles.Initializers.SetVectorAttributeToVectorExpression;
using OperatorVectorExpression = ValveResourceFormat.Particles.Operators.SetVectorAttributeToVectorExpression;

namespace Tests;

public class ParticleVectorExpressionTest
{
    [Test]
    public async Task FactorySupportsBothVectorExpressionClasses()
    {
        await Assert.That(ParticleSupportInfo.IsInitializerSupported("C_INIT_SetVectorAttributeToVectorExpression")).IsTrue();
        await Assert.That(ParticleSupportInfo.IsOperatorSupported("C_OP_SetVectorAttributeToVectorExpression")).IsTrue();
    }

    [Test]
    public async Task TheOperatorFloorsSubnormalDivisorsAndSaturatesLerp()
    {
        var quotient = VectorExpressionEvaluator.Evaluate(
            VectorExpression.VECTOR_EXPRESSION_DIVIDE,
            new Vector3(1f, -2f, 3f),
            new Vector3(0f, -0f, 1e-40f),
            0f,
            operatorPath: true);
        var lerped = VectorExpressionEvaluator.Evaluate(
            VectorExpression.VECTOR_EXPRESSION_LERP,
            Vector3.Zero,
            Vector3.One,
            2f,
            operatorPath: true);

        await Assert.That(quotient.X).IsBetween(8388608f * 0.999f, 8388608f * 1.001f);
        await Assert.That(quotient.Y).IsBetween(16777216f * 0.999f, 16777216f * 1.001f);
        await Assert.That(quotient.Z).IsBetween(24953544f * 0.999f, 24953544f * 1.001f);
        await Assert.That(lerped).IsEqualTo(Vector3.One);
    }

    [Test]
    public async Task TheInitializerDividesPlainlyAndLerpsWithoutSaturation()
    {
        var quotient = VectorExpressionEvaluator.Evaluate(
            VectorExpression.VECTOR_EXPRESSION_DIVIDE,
            new Vector3(1f, 1f, 3f),
            new Vector3(2f, 4f, 8f),
            0f,
            operatorPath: false);
        var lerped = VectorExpressionEvaluator.Evaluate(
            VectorExpression.VECTOR_EXPRESSION_LERP,
            Vector3.Zero,
            Vector3.One,
            2f,
            operatorPath: false);

        await Assert.That(quotient).IsEqualTo(new Vector3(0.5f, 0.25f, 0.375f));
        await Assert.That(lerped).IsEqualTo(new Vector3(2f, 2f, 2f));
    }

    [Test]
    public async Task AnUninitializedExpressionKeepsTheCurrentFieldValueInTheInitializer()
    {
        var data = FunctionData();
        data["m_nExpression"] = new KVObject("VECTOR_EXPRESSION_UNINITIALIZED");
        data["m_nOutputField"] = new KVObject((int)ParticleField.Color);
        var particles = Collection(new Particle { Color = new Vector3(0.25f) });

        new InitVectorExpression(Parser(data)).Initialize(ref particles.Current[0], particles, new ParticleSystemState());

        await Assert.That(particles.Current[0].Color).IsEqualTo(new Vector3(0.25f));
    }

    [Test]
    public async Task ClampsFractionBeforeNormalizingForInitializerAndOperator()
    {
        var data = FunctionData();
        data["m_nExpression"] = new KVObject("VECTOR_EXPRESSION_INPUT_1");
        data["m_vInput1"] = LiteralVector(2f, -2f, 0f);
        data["m_nOutputField"] = new KVObject((int)ParticleField.Color);
        data["m_bNormalizedOutput"] = new KVObject(true);

        var particles = Collection(new Particle { Color = new Vector3(0.25f) });
        var state = new ParticleSystemState();
        var initializer = new InitVectorExpression(Parser(data));
        initializer.Initialize(ref particles.Current[0], particles, state);

        await Assert.That(particles.Current[0].Color).IsEqualTo(Vector3.UnitX);

        particles.Current[0].Color = new Vector3(0.25f);
        var particleOperator = new OperatorVectorExpression(Parser(data));
        particleOperator.Operate(particles, 0.1f, state, 1f);

        await Assert.That(particles.Current[0].Color).IsEqualTo(Vector3.UnitX);
    }

    [Test]
    public async Task OperatorAppliesStrengthBeforeTheSetMethod()
    {
        var data = FunctionData();
        data["m_nExpression"] = new KVObject("VECTOR_EXPRESSION_INPUT_1");
        data["m_vInput1"] = LiteralVector(2f, 4f, 6f);
        data["m_nOutputField"] = new KVObject((int)ParticleField.ScratchVector);
        data["m_nSetMethod"] = new KVObject("PARTICLE_SET_ADD_TO_CURRENT_VALUE");
        var particles = Collection(new Particle { ScratchVector = new Vector3(10f) });

        new OperatorVectorExpression(Parser(data)).Operate(
            particles, 0.1f, new ParticleSystemState(), 0.25f);

        await Assert.That(particles.Current[0].ScratchVector).IsEqualTo(new Vector3(10.5f, 11f, 11.5f));
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
