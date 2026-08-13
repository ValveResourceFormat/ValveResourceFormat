using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles;

/// <summary>
/// Wraps a <see cref="KVObject"/> to provide typed, default-value-aware accessors for particle system definition properties.
/// </summary>
/// <param name="Data">The block being read.</param>
/// <param name="Logger">Where parse warnings go.</param>
/// <param name="InputOrdinal">
/// Numbers the inputs of one particle function as they are parsed, shared by every nested block of
/// that function. Held in a cell because the parser is copied by value.
/// </param>
record struct ParticleDefinitionParser(KVObject Data, ILogger Logger, int[] InputOrdinal)
{
    /// <summary>Reads a particle function, starting its input numbering over.</summary>
    public ParticleDefinitionParser(KVObject data, ILogger logger) : this(data, logger, new int[1])
    {
    }

    /// <summary>Reads a block nested in this one, continuing its input numbering.</summary>
    public readonly ParticleDefinitionParser Nested(KVObject data) => new(data, Logger, InputOrdinal);

    /// <summary>
    /// Claims the next displacement for an input whose draw is constant per particle, so that two
    /// inputs of one function reading the same particle land on different slots of the shared random
    /// table. Different functions are already separated by
    /// <see cref="Utils.ParticleRandom.OperatorOffset"/>.
    /// </summary>
    public readonly int NextInputOrdinal() => InputOrdinal[0]++;

    private readonly T GetValueOrDefault<T>(string key, Func<string, T?> parsingMethod, T @default)
    {
        if (Data.ContainsKey(key))
        {
            return parsingMethod(key) ?? @default;
        }

        return @default;
    }

    /// <summary>
    /// Returns an array of child parsers for the sub-collection array at <paramref name="k"/>, or an empty array if the key is absent.
    /// </summary>
    public readonly ParticleDefinitionParser[] Array(string k)
    {
        if (!Data.ContainsKey(k))
        {
            return [];
        }

        var logger = Logger; // Copy to local variable to avoid capturing 'this' in lambda
        var ordinal = InputOrdinal;
        return [.. Data.GetArray(k).Select(item => new ParticleDefinitionParser(item, logger, ordinal))];
    }

    private readonly float Float(string k)
    {
        // Newer content authors many scalar fields as full float-input structures; the call site
        // must read those with NumberProvider instead of as a literal.
        if (Data.GetSubCollection(k) is { IsCollection: true })
        {
            Logger.LogWarning("Field {Key} is authored as a number provider, but is parsed as a literal float; it should be read with NumberProvider", k);
        }

        return Data.GetFloatProperty(k);
    }

    /// <summary>Reads a float property, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly float Float(string key, float @default = default) => GetValueOrDefault(key, Float, @default);

    private readonly int Int32(string k)
    {
        if (Data.GetSubCollection(k) is { IsCollection: true })
        {
            Logger.LogWarning("Field {Key} is authored as a number provider, but is parsed as a literal int; it should be read with NumberProvider", k);
        }

        return Data.GetInt32Property(k);
    }

    /// <summary>Reads an int property, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly int Int32(string key, int @default = default) => GetValueOrDefault(key, Int32, @default);

    private readonly long Long(string k) => Data.GetIntegerProperty(k);
    /// <summary>Reads a long property, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly long Long(string key, long @default = default) => GetValueOrDefault(key, Long, @default);

    private readonly bool Boolean(string k) => Data.GetBooleanProperty(k);
    /// <summary>Reads a bool property, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly bool Boolean(string key, bool @default = default) => GetValueOrDefault(key, Boolean, @default);

    private readonly Vector3 Vector3(string k)
    {
        // Some content authors vectors as space-separated strings ("0.7 0.5 0.25").
        if (Data.TryGetValue(k, out var value) && value.ValueType == KVValueType.String)
        {
            return EntityTransformHelper.ParseVector3(Data.GetStringProperty(k));
        }

        var sub = Data.GetSubCollection(k);

        // Newer content authors some vector fields as full vector-input structures; the call site
        // must read those with VectorProvider instead of as a literal.
        if (sub.ContainsKey("m_nType"))
        {
            Logger.LogWarning("Field {Key} is authored as a vector provider, but is parsed as a literal vector; it should be read with VectorProvider", k);
        }

        return sub.ToVector3();
    }
    /// <summary>Reads a <see cref="System.Numerics.Vector3"/> sub-collection property, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly Vector3 Vector3(string key, Vector3 @default = default) => GetValueOrDefault(key, Vector3, @default);

    /// <summary>Reads an enum property, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly T Enum<T>(string key, T @default = default) where T : struct, Enum
        => ReadEnum(key, @default, normalize: false);

    /// <summary>Reads an enum property with normalized name matching, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly T EnumNormalized<T>(string key, T @default = default) where T : struct, Enum
        => ReadEnum(key, @default, normalize: true);

    /// <summary>
    /// Reads an enum authored either as a member name or as its raw integer value, falling back to
    /// <paramref name="default"/> for anything this enum does not define.
    /// </summary>
    private readonly T ReadEnum<T>(string key, T @default, bool normalize) where T : struct, Enum
    {
        if (!Data.TryGetValue(key, out var value))
        {
            return @default;
        }

        if (value.ValueType != KVValueType.String)
        {
            return FromEnumValue(key, (int)value, @default);
        }

        var name = (string)value;

        if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return FromEnumValue(key, numeric, @default);
        }

        var member = normalize ? KVObjectExtensions.NormalizeEnumName<T>(name, "Flags") : name;

        if (System.Enum.TryParse<T>(member, false, out var result))
        {
            return result;
        }

        Logger.LogUniqueWarning("Enum {Enum} has no member named '{Name}' read from {Key}, using {Default}",
            typeof(T).Name, name, key, @default);

        return @default;
    }

    private readonly T FromEnumValue<T>(string key, int value, T @default) where T : struct, Enum
    {
        var enumValue = (T)(object)value;

        if (System.Enum.IsDefined(enumValue))
        {
            return enumValue;
        }

        Logger.LogUniqueWarning("Enum {Enum} has no member with value {Value} read from {Key}, using {Default}",
            typeof(T).Name, value, key, @default);

        return @default;
    }

    private readonly ParticleField ParticleField(string k) => (ParticleField)Data.GetIntegerProperty(k);
    /// <summary>Reads a particle field enum property, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly ParticleField ParticleField(string key, ParticleField @default = default) => GetValueOrDefault(key, ParticleField, @default);

    /// <summary>Reads an integer RGB color array into a normalized [0, 1] <see cref="System.Numerics.Vector3"/>, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly Vector3 Color24(string key, Vector3 @default = default) => GetValueOrDefault(key, Color24, @default);
    private readonly Vector3 Color24(string k)
    {
        var vectorValues = Data.GetIntegerArray(k);
        return new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
    }

    /// <summary>Reads and constructs an <see cref="INumberProvider"/> from the property at <paramref name="key"/>, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly INumberProvider NumberProvider(string key, INumberProvider @default) => GetValueOrDefault(key, NumberProvider, @default);
    private readonly INumberProvider? NumberProvider(string key)
    {
        var pfParameters = Data.GetSubCollection(key);

        if (pfParameters.IsNull)
        {
            return null;
        }

        if (pfParameters.IsCollection)
        {
            var type = pfParameters.GetStringProperty("m_nType");
            var parse = Nested(pfParameters);

            switch (type)
            {
                case "PF_TYPE_LITERAL":
                    return new LiteralNumberProvider(parse.Float("m_flLiteralValue"));
                case "PF_TYPE_RANDOM_UNIFORM":
                    return new RandomNumberProvider(parse, false);
                case "PF_TYPE_RANDOM_BIASED":
                    return new RandomNumberProvider(parse, true);
                case "PF_TYPE_COLLECTION_AGE":
                    return new CollectionAgeNumberProvider(parse);
                case "PF_TYPE_ENDCAP_AGE":
                    return new EndCapAgeNumberProvider(parse);
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
                case "PF_TYPE_CONTROL_POINT_SPEED":
                    return new ControlPointSpeedNumberProvider(parse);
                // KNOWN TYPES WE DON'T SUPPORT:
                // PF_TYPE_CONTROL_POINT_CHANGE_AGE - no way.
                // PF_TYPE_PARTICLE_NOISE - exists only in deskjob and CS2. Likely added in behavior version 11 or 12.
                // PF_TYPE_NAMED_VALUE - seen in dota's particle.dll?? not in deskjob's, so in behavior version 13+?
                default:
                    if (pfParameters.ContainsKey("m_flLiteralValue"))
                    {
                        Logger.LogWarning("Number provider of type {Type} is not directly supported, but it has m_flLiteralValue", type);
                        return new LiteralNumberProvider(pfParameters.GetFloatProperty("m_flLiteralValue"));
                    }

                    if (type == null)
                    {
                        // Old serialization omits every default-valued key, including m_nType; fall back to the
                        // caller-supplied default.
                        Logger.LogWarning("Number provider has no m_nType and no m_flLiteralValue, using the caller default");
                        return null;
                    }

                    throw new InvalidCastException($"Could not create number provider of type {type}.");
            }
        }
        else
        {
            return new LiteralNumberProvider((float)pfParameters);
        }
    }

    /// <summary>Reads and constructs an <see cref="IVectorProvider"/> from the property at <paramref name="key"/>, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly IVectorProvider VectorProvider(string key, IVectorProvider @default) => GetValueOrDefault(key, VectorProvider, @default);
    private readonly IVectorProvider VectorProvider(string key)
    {
        var pvecParameters = Data.GetSubCollection(key);

        if (pvecParameters.IsCollection && pvecParameters.ContainsKey("m_nType"))
        {
            var type = pvecParameters.GetStringProperty("m_nType");
            var parse = Nested(pvecParameters);

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
                case "PVEC_TYPE_CP_DELTA":
                    return new CPDeltaVectorProvider(parse);
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
                case "PVEC_TYPE_RANDOM_UNIFORM":
                    return new RandomUniformVectorProvider(parse);
                case "PVEC_TYPE_RANDOM_UNIFORM_OFFSET":
                    return new RandomUniformOffsetVectorProvider(parse);
                /* UNSUPPORTED:
                 * PVEC_TYPE_NAMED_VALUE - new in dota
                 * PVEC_TYPE_PARTICLE_VELOCITY - new in dota
                 * PVEC_TYPE_CP_RELATIVE_RANDOM_DIR - new in dota. presumably relative dir but the value is random per particle?
                 */
                default:
                    if (pvecParameters.ContainsKey("m_vLiteralValue"))
                    {
                        Logger.LogWarning("Vector provider of type {Type} is not directly supported, but it has m_vLiteralValue", type);
                        return new LiteralVectorProvider(parse.Vector3("m_vLiteralValue"));
                    }

                    throw new InvalidCastException($"Could not create vector provider of type {type}.");
            }
        }

        return new LiteralVectorProvider(Vector3(key));
    }

    /// <summary>Reads and constructs an <see cref="ITransformProvider"/> from the property at <paramref name="key"/>, returning <paramref name="default"/> if the key is absent.</summary>
    public readonly ITransformProvider TransformInput(string key, ITransformProvider @default) => GetValueOrDefault(key, TransformInput, @default);
    private readonly ITransformProvider TransformInput(string key)
    {
        var transformParameters = Data.GetSubCollection(key);

        if (transformParameters.IsCollection)
        {
            var type = transformParameters.GetStringProperty("m_nType", "PT_TYPE_CONTROL_POINT");
            var parse = Nested(transformParameters);

            switch (type)
            {
                case "PT_TYPE_CONTROL_POINT":
                    {
                        var controlPoint = parse.Int32("m_nControlPoint");
                        var useOrientation = parse.Boolean("m_bUseOrientation", true);
                        return new ControlPointTransformProvider(controlPoint, useOrientation);
                    }
                case "PT_TYPE_CONTROL_POINT_RANGE":
                    // TODO: Implement range support if needed
                    Logger.LogWarning("PT_TYPE_CONTROL_POINT_RANGE not fully supported, using first CP only");
                    {
                        var controlPoint = parse.Int32("m_nControlPoint");
                        var useOrientation = parse.Boolean("m_bUseOrientation", true);
                        return new ControlPointTransformProvider(controlPoint, useOrientation);
                    }
                case "PT_TYPE_INVALID":
                case "PT_TYPE_NAMED_VALUE":
                default:
                    Logger.LogWarning("Transform type {Type} not supported, using identity transform", type);
                    return new IdentityTransformProvider();
            }
        }

        return new IdentityTransformProvider();
    }
}
