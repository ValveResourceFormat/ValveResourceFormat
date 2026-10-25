using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Reduces a float attribute over all live particles to one statistic, remaps it through
    /// <c>m_flOutputRemap</c> and writes it into one component of a control point. Nothing is written
    /// while the system holds no particles.
    /// </summary>
    /// <remarks>
    /// The standard deviation is the population one. The mode rounds values to
    /// <c>m_flDecimalPlaces</c> decimals to count them, and returns the unrounded value of the first
    /// particle to reach the winning count. The maximum starts at the smallest positive float, so a
    /// field that is nowhere positive reports that.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapAverageScalarValuetoCP">C_OP_RemapAverageScalarValuetoCP</seealso>
    class RemapAverageScalarValuetoCP : ParticleFunctionPreEmissionOperator
    {
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/SetStatisticExpressionType_t">SetStatisticExpressionType_t</seealso>
        private enum StatisticExpression
        {
            SET_EXPRESSION_UNINITIALIZED = -1,
            SET_EXPRESSION_SUM = 0,
            SET_EXPRESSION_MEAN = 1,
            SET_EXPRESSION_MEDIAN = 2,
            SET_EXPRESSION_MODE = 3,
            SET_EXPRESSION_STANDARD_DEVIATION = 4,
            SET_EXPRESSION_MIN = 5,
            SET_EXPRESSION_MAX = 6,
        }

        private readonly StatisticExpression expression = StatisticExpression.SET_EXPRESSION_MEAN;
        private readonly INumberProvider decimalPlaces = new LiteralNumberProvider(2f);
        private readonly int outControlPoint = 1;
        private readonly int outVectorField;
        private readonly ParticleField field = ParticleField.Radius;
        private readonly AttributeMapping? outputRemap;

        private readonly List<float> values = [];
        private readonly Dictionary<int, int> modeCounts = [];

        public RemapAverageScalarValuetoCP(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum("m_nExpression", expression);
            decimalPlaces = parse.NumberProvider("m_flDecimalPlaces", decimalPlaces);
            outControlPoint = parse.Int32("m_nOutControlPointNumber", outControlPoint);
            outVectorField = parse.Int32("m_nOutVectorField", outVectorField);
            field = parse.ParticleField("m_nField", field);
            outputRemap = AttributeMapping.FromRemapInput(parse, "m_flOutputRemap");
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var particles = particleSystemState.Data?.Particles;

            if (particles == null || particles.Count == 0 || outVectorField is < 0 or > 2)
            {
                return;
            }

            values.Clear();

            values.AddRange(particles.Current.ReadFloats(field));

            var statistic = expression switch
            {
                StatisticExpression.SET_EXPRESSION_SUM => Sum(),
                StatisticExpression.SET_EXPRESSION_MEAN => Sum() / values.Count,
                StatisticExpression.SET_EXPRESSION_MEDIAN => Median(),
                StatisticExpression.SET_EXPRESSION_MODE => Mode(decimalPlaces.NextNumber(particleSystemState)),
                StatisticExpression.SET_EXPRESSION_STANDARD_DEVIATION => StandardDeviation(),
                StatisticExpression.SET_EXPRESSION_MIN => MathF.Min(float.MaxValue, TensorPrimitives.Min(CollectionsMarshal.AsSpan(values))),
                StatisticExpression.SET_EXPRESSION_MAX => MathF.Max(ParticleMath.FloatMin, TensorPrimitives.Max(CollectionsMarshal.AsSpan(values))),
                _ => 0f,
            };

            var value = outputRemap?.ApplyMapping(statistic) ?? statistic;
            particleSystemState.SetControlPointValueComponent(outControlPoint, outVectorField, value);
        }

        private float Sum() => TensorPrimitives.Sum(CollectionsMarshal.AsSpan(values));

        private float Median()
        {
            values.Sort();

            var half = values.Count / 2;

            return values.Count % 2 == 1 ? values[half] : (values[half - 1] + values[half]) * 0.5f;
        }

        private float StandardDeviation()
        {
            var mean = Sum() / values.Count;
            var deviations = CollectionsMarshal.AsSpan(values);

            TensorPrimitives.Subtract(deviations, mean, deviations);

            return MathF.Sqrt(TensorPrimitives.SumOfSquares(deviations) / values.Count);
        }

        private float Mode(float places)
        {
            var scale = MathF.Pow(10f, MathF.Floor(places));
            var best = -1;
            var result = -1f;

            modeCounts.Clear();

            foreach (var value in values)
            {
                var key = (int)MathF.Round(scale * value, MidpointRounding.AwayFromZero);
                var count = modeCounts.GetValueOrDefault(key) + 1;
                modeCounts[key] = count;

                if (count > best)
                {
                    best = count;
                    result = value;
                }
            }

            return result;
        }
    }
}
