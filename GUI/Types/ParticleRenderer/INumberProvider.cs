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
