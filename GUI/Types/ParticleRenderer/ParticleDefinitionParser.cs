using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Types.ParticleRenderer;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;
using System.Linq;

record struct ParticleDefinitionParser(IKeyValueCollection Data)
{
    public readonly T GetValueOrDefault<T>(string key, Func<string, T> parsingMethod, T @default)
    {
        if (Data.ContainsKey(key))
        {
            return parsingMethod(key);
        }

        return @default;
    }

    public readonly ParticleDefinitionParser[] Array(string k)
    {
        if (!Data.ContainsKey(k))
        {
            return System.Array.Empty<ParticleDefinitionParser>();
        }

        return Data.GetArray(k).Select(item => new ParticleDefinitionParser(item)).ToArray();
    }

    public readonly float Float(string k) => Data.GetFloatProperty(k);
    public readonly float Float(string key, float @default) => GetValueOrDefault(key, Float, @default);

    public readonly int Int32(string k) => Data.GetInt32Property(k);
    public readonly int Int32(string key, int @default) => GetValueOrDefault(key, Int32, @default);

    public readonly long Long(string k) => Data.GetIntegerProperty(k);
    public readonly long Long(string key, long @default) => GetValueOrDefault(key, Long, @default);

    public readonly bool Boolean(string k) => Data.GetProperty<bool>(k);
    public readonly bool Boolean(string key, bool @default) => GetValueOrDefault(key, Boolean, @default);

    public readonly Vector3 Vector3(string k) => Data.GetSubCollection(k).ToVector3();
    public readonly Vector3 Vector3(string key, Vector3 @default) => GetValueOrDefault(key, Vector3, @default);

    public readonly INumberProvider NumberProvider(string k) => Data.GetNumberProvider(k);
    public readonly INumberProvider NumberProvider(string key, INumberProvider @default) => GetValueOrDefault(key, NumberProvider, @default);

    public readonly IVectorProvider VectorProvider(string v) => Data.GetVectorProvider(v);
    public readonly IVectorProvider VectorProvider(string key, IVectorProvider @default) => GetValueOrDefault(key, VectorProvider, @default);

    public readonly T Enum<T>(string k) where T : Enum => Data.GetEnumValue<T>(k);
    public readonly T Enum<T>(string key, T @default) where T : Enum
        => GetValueOrDefault(key, Enum<T>, @default);

    public readonly T EnumNormalized<T>(string k) where T : Enum => Data.GetEnumValue<T>(k, true);
    public readonly T EnumNormalized<T>(string key, T @default) where T : Enum
        => GetValueOrDefault(key, EnumNormalized<T>, @default);

    public readonly ParticleField ParticleField(string k) => (ParticleField)Data.GetIntegerProperty(k);
    public readonly ParticleField ParticleField(string key, ParticleField @default) => GetValueOrDefault(key, ParticleField, @default);
}
