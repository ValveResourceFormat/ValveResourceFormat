using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    public interface INumberProvider
    {
        double NextNumber();
    }

    public class LiteralNumberProvider : INumberProvider
    {
        private readonly double value;

        public LiteralNumberProvider(double value)
        {
            this.value = value;
        }

        public double NextNumber() => value;
    }

#pragma warning disable SA1402 // File may only contain a single type
    public static class INumberProviderExtensions
#pragma warning restore SA1402 // File may only contain a single type
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
                        throw new InvalidCastException($"Could not create number provider of type {type}.");
                }
            }
            else
            {
                return new LiteralNumberProvider(Convert.ToDouble(property));
            }
        }

        public static int NextInt(this INumberProvider numberProvider)
        {
            return (int)numberProvider.NextNumber();
        }
    }
}
