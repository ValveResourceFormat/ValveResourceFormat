using System;
using System.Globalization;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    public interface INumberProvider
    {
        double NextNumber();
    }

    public readonly struct LiteralNumberProvider : INumberProvider
    {
        private readonly double value;

        public LiteralNumberProvider(double value)
        {
            this.value = value;
        }

        public double NextNumber() => value;
    }

    public readonly struct RandomNumberProvider : INumberProvider
    {
        private readonly double min;
        private readonly double max;

        public RandomNumberProvider(double min, double max)
        {
            this.min = min;
            this.max = max;
        }

        public double NextNumber() => min + (Random.Shared.NextDouble() * (max - min));
    }

    public static class INumberProviderExtensions
    {
        public static INumberProvider GetNumberProvider(this IKeyValueCollection keyValues, string propertyName)
        {
            var property = keyValues.GetProperty<object>(propertyName);

            if (property is IKeyValueCollection numberProviderParameters)
            {
                var type = numberProviderParameters.GetProperty<string>("m_nType");
                switch (type)
                {
                    case "PF_TYPE_LITERAL":
                        return new LiteralNumberProvider(numberProviderParameters.GetDoubleProperty("m_flLiteralValue"));

                    case "PF_TYPE_RANDOM_BIASED":
                        // TODO: Implement biased random
                        return new RandomNumberProvider(
                            numberProviderParameters.GetDoubleProperty("m_flRandomMin"),
                            numberProviderParameters.GetDoubleProperty("m_flRandomMax")
                        );

                    case "PF_TYPE_RANDOM_UNIFORM":
                        if (numberProviderParameters.GetStringProperty("m_nRandomMode") != "PF_RANDOM_MODE_CONSTANT")
                        {
                            Console.Error.WriteLine($"Unsupported random number provider with random mode {numberProviderParameters.GetStringProperty("m_nRandomMode")}");
                        }

                        return new RandomNumberProvider(
                            numberProviderParameters.GetDoubleProperty("m_flRandomMin"),
                            numberProviderParameters.GetDoubleProperty("m_flRandomMax")
                        );

                    default:
                        if (numberProviderParameters.ContainsKey("m_flLiteralValue"))
                        {
                            Console.Error.WriteLine($"Number provider of type {type} is not directly supported, but it has m_flLiteralValue.");
                            return new LiteralNumberProvider(numberProviderParameters.GetDoubleProperty("m_flLiteralValue"));
                        }

                        throw new InvalidCastException($"Could not create number provider of type {type}.");
                }
            }
            else
            {
                return new LiteralNumberProvider(Convert.ToDouble(property, CultureInfo.InvariantCulture));
            }
        }

        public static int NextInt(this INumberProvider numberProvider)
        {
            return (int)numberProvider.NextNumber();
        }
    }
}
