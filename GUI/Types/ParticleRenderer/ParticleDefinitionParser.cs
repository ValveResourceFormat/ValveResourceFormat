using System.Globalization;
using System.Linq;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.ParticleRenderer;

record struct ParticleDefinitionParser(KVObject Data)
{
    private readonly T GetValueOrDefault<T>(string key, Func<string, T> parsingMethod, T @default)
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
            return [];
        }

        return [.. Data.GetArray(k).Select(static item => new ParticleDefinitionParser(item))];
    }

    private readonly float Float(string k) => Data.GetFloatProperty(k);
    public readonly float Float(string key, float @default = default) => GetValueOrDefault(key, Float, @default);

    private readonly int Int32(string k) => Data.GetInt32Property(k);
    public readonly int Int32(string key, int @default = default) => GetValueOrDefault(key, Int32, @default);

    private readonly long Long(string k) => Data.GetIntegerProperty(k);
    public readonly long Long(string key, long @default = default) => GetValueOrDefault(key, Long, @default);

    private readonly bool Boolean(string k) => Data.GetProperty<bool>(k);
    public readonly bool Boolean(string key, bool @default = default) => GetValueOrDefault(key, Boolean, @default);

    private readonly Vector3 Vector3(string k) => Data.GetSubCollection(k).ToVector3();
    public readonly Vector3 Vector3(string key, Vector3 @default = default) => GetValueOrDefault(key, Vector3, @default);

    private readonly T Enum<T>(string k) where T : Enum => Data.GetEnumValue<T>(k);
    public readonly T Enum<T>(string key, T @default = default) where T : struct, Enum
        => GetValueOrDefault(key, Enum<T>, @default);

    private readonly T EnumNormalized<T>(string k) where T : Enum => Data.GetEnumValue<T>(k, true);
    public readonly T EnumNormalized<T>(string key, T @default = default) where T : struct, Enum
        => GetValueOrDefault(key, EnumNormalized<T>, @default);

    private readonly ParticleField ParticleField(string k) => (ParticleField)Data.GetIntegerProperty(k);
    public readonly ParticleField ParticleField(string key, ParticleField @default = default) => GetValueOrDefault(key, ParticleField, @default);

    public readonly Vector3 Color24(string key, Vector3 @default = default) => GetValueOrDefault(key, Color24, @default);
    private readonly Vector3 Color24(string k)
    {
        var vectorValues = Data.GetIntegerArray(k);
        return new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
    }

    public readonly INumberProvider NumberProvider(string key, INumberProvider @default) => GetValueOrDefault(key, NumberProvider, @default);
    private readonly INumberProvider NumberProvider(string key)
    {
        var property = Data.GetProperty<object>(key);

        if (property is KVObject pfParameters)
        {
            var type = pfParameters.GetProperty<string>("m_nType");
            var parse = new ParticleDefinitionParser(pfParameters);

            switch (type)
            {
                case "PF_TYPE_LITERAL":
                    return new LiteralNumberProvider(parse.Float("m_flLiteralValue"));
                case "PF_TYPE_RANDOM_UNIFORM":
                    return new RandomNumberProvider(parse, false);
                case "PF_TYPE_RANDOM_BIASED":
                    return new RandomNumberProvider(parse, true);
                case "PF_TYPE_COLLECTION_AGE":
                    return new CollectionAgeNumberProvider();
                case "PF_TYPE_CONTROL_POINT_COMPONENT":
                    return new ControlPointComponentNumberProvider(parse);
                case "PF_TYPE_PARTICLE_DETAIL_LEVEL":
                    return new DetailLevelNumberProvider(parse);
                case "PF_TYPE_PARTICLE_AGE":
                    return new ParticleAgeNumberProvider(parse);
                case "PF_TYPE_PARTICLE_AGE_NORMALIZED":
                    return new ParticleAgeNormalizedNumberProvider(parse);
                case "PF_TYPE_PARTICLE_FLOAT":
                    return new PerParticleNumberProvider(parse);
                case "PF_TYPE_PARTICLE_VECTOR_COMPONENT":
                    return new PerParticleVectorComponentNumberProvider(parse);
                case "PF_TYPE_PARTICLE_SPEED":
                    return new PerParticleSpeedNumberProvider(parse);
                case "PF_TYPE_PARTICLE_NUMBER":
                    return new PerParticleCountNumberProvider(parse);
                case "PF_TYPE_PARTICLE_NUMBER_NORMALIZED":
                    return new PerParticleCountNormalizedNumberProvider(parse);
                // KNOWN TYPES WE DON'T SUPPORT:
                // PF_TYPE_ENDCAP_AGE - unsupported because we don't support endcaps
                // PF_TYPE_CONTROL_POINT_COMPONENT - todo?
                // PF_TYPE_CONTROL_POINT_CHANGE_AGE - no way.
                // PF_TYPE_CONTROL_POINT_SPEED - new in cs2? def not going to support this
                // PF_TYPE_PARTICLE_NOISE - exists only in deskjob and CS2. Likely added in behavior version 11 or 12.
                // PF_TYPE_NAMED_VALUE - seen in dota's particle.dll?? not in deskjob's, so in behavior version 13+?
                default:
                    if (pfParameters.ContainsKey("m_flLiteralValue"))
                    {
                        Log.Warn(nameof(ParticleDefinitionParser), $"Number provider of type {type} is not directly supported, but it has m_flLiteralValue.");
                        return new LiteralNumberProvider(pfParameters.GetFloatProperty("m_flLiteralValue"));
                    }

                    throw new InvalidCastException($"Could not create number provider of type {type}.");
            }
        }
        else
        {
            return new LiteralNumberProvider((float)Convert.ToDouble(property, CultureInfo.InvariantCulture));
        }
    }

    public readonly IVectorProvider VectorProvider(string key, IVectorProvider @default) => GetValueOrDefault(key, VectorProvider, @default);
    private readonly IVectorProvider VectorProvider(string key)
    {
        var property = Data.GetProperty<object>(key);

        if (property is KVObject pvecParameters && pvecParameters.ContainsKey("m_nType"))
        {
            var type = pvecParameters.GetProperty<string>("m_nType");
            var parse = new ParticleDefinitionParser(pvecParameters);

            switch (type)
            {
                case "PVEC_TYPE_LITERAL":
                    return new LiteralVectorProvider(parse.Vector3("m_vLiteralValue"));
                case "PVEC_TYPE_LITERAL_COLOR":
                    return new LiteralColorVectorProvider(parse.Vector3("m_LiteralColor"));
                case "PVEC_TYPE_PARTICLE_VECTOR":
                    return new PerParticleVectorProvider(parse);
                case "PVEC_TYPE_PARTICLE_VELOCITY":
                    return new ParticleVelocityVectorProvider();
                case "PVEC_TYPE_CP_VALUE":
                    return new CPValueVectorProvider(parse);
                case "PVEC_TYPE_CP_RELATIVE_POSITION":
                    return new CPRelativePositionProvider(parse);
                case "PVEC_TYPE_CP_RELATIVE_DIR":
                    return new CPRelativeDirectionProvider(parse);
                case "PVEC_TYPE_FLOAT_COMPONENTS":
                    return new FloatComponentsVectorProvider(parse);
                case "PVEC_TYPE_FLOAT_INTERP_CLAMPED":
                    return new FloatInterpolationVectorProvider(parse, true);
                case "PVEC_TYPE_FLOAT_INTERP_OPEN":
                    return new FloatInterpolationVectorProvider(parse, false);
                case "PVEC_TYPE_FLOAT_INTERP_GRADIENT":
                    return new ColorGradientVectorProvider(parse);
                /* UNSUPPORTED:
                 * PVEC_TYPE_NAMED_VALUE - new in dota
                 * PVEC_TYPE_PARTICLE_VELOCITY - new in dota
                 * PVEC_TYPE_CP_RELATIVE_RANDOM_DIR - new in dota. presumably relative dir but the value is random per particle?
                 * PVEC_TYPE_RANDOM_UNIFORM - new in dota. uses vRandomMin and vRandomMax
                 * PVEC_TYPE_RANDOM_UNIFORM_OFFSET - new in dota
                 */
                default:
                    if (pvecParameters.ContainsKey("m_vLiteralValue"))
                    {
                        Log.Warn(nameof(ParticleDefinitionParser), $"Vector provider of type {type} is not directly supported, but it has m_vLiteralValue.");
                        return new LiteralVectorProvider(parse.Vector3("m_vLiteralValue"));
                    }

                    throw new InvalidCastException($"Could not create vector provider of type {type}.");
            }
        }

        return new LiteralVectorProvider(Vector3(key));
    }
}
