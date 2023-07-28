using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

record struct ParticleSystemDefinitionParser(IKeyValueCollection Data)
{
    public readonly T GetValueOrDefault<T>(string key, Func<string, T> parsingMethod, T @default)
    {
        if (Data.ContainsKey(key))
        {
            return parsingMethod(key);
        }

        return @default;
    }

    public readonly float Float(string k) => Data.GetFloatProperty(k);
    public readonly float Float(string key, float @default) => GetValueOrDefault(key, Float, @default);

    public readonly int Int32(string k) => Data.GetInt32Property(k);
    public readonly int Int32(string key, int @default) => GetValueOrDefault(key, Int32, @default);

    public readonly Vector3 Vector3(string k) => Data.GetSubCollection(k).ToVector3();
    public readonly Vector3 Vector3(string key, Vector3 @default) => GetValueOrDefault(key, Vector3, @default);
}
