using System;
using System.Globalization;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    interface INumberProvider
    {
        double NextNumber();
    }

    readonly struct LiteralNumberProvider : INumberProvider
    {
        private readonly double value;

        public LiteralNumberProvider(double value)
        {
            this.value = value;
        }

        public double NextNumber() => value;
    }

    readonly struct RandomNumberProvider : INumberProvider
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

    readonly struct BiasedRandomNumberProvider : INumberProvider
    {
        private readonly RandomNumberProvider rng;
        private readonly double exponent;

        public BiasedRandomNumberProvider(double min, double max, double exponent)
        {
            this.exponent = exponent;
            this.rng = new RandomNumberProvider(min, max);
        }

        public double NextNumber() => Math.Pow(rng.NextNumber(), exponent);
    }

    static class INumberProviderExtensions
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
                        return new BiasedRandomNumberProvider(
                            numberProviderParameters.GetDoubleProperty("m_flRandomMin"),
                            numberProviderParameters.GetDoubleProperty("m_flRandomMax"),
                            numberProviderParameters.GetDoubleProperty("m_flBiasParameter")
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

                    case "PF_TYPE_CONTROL_POINT_COMPONENT":
                        // No control points in our renderer (yet?), falling back to 0
                        return new LiteralNumberProvider(0);

                    case "PF_TYPE_PARTICLE_FLOAT":
                        // idk what this is
                        return new LiteralNumberProvider(0);

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
