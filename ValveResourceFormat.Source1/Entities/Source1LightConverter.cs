using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Entities;

/// <summary>
/// Converts the light entities of a Source 1 map to the keyvalues the Source 2 light entities read.
/// </summary>
/// <remarks>
/// Source 1 gives a light as a gamma space color with an intensity in 0-255 units. Source 2 takes a gamma
/// space color normalized to its brightest channel and a linear brightness, where 1 lights a surface fully
/// 100 units away from the light. The attenuation terms are written as they are and the renderer divides the
/// brightness by their value at 100 units on its own.
/// </remarks>
internal static class Source1LightConverter
{
    private const float IntensityUnit = 255f;
    private const float Gamma = 2.2f;
    private const float ReferenceDistance = 100f;
    private const float ReachIntensityRatio = 256f;
    private const float DefaultInnerCone = 10f;
    private const float MaxConeAngle = 90f;
    private const float DefaultDynamicLightDistance = 120f;
    private const float MinAttenuationTerm = 0.001f;
    private const float MinWrittenAttenuationTerm = 0.002f;
    private const string DefaultSunLight = "255 255 255 200";

    public static bool IsLightClass(string classname)
        => classname is "light" or "light_spot" or "light_environment" or "light_dynamic" or "env_projectedtexture";

    /// <summary>Writes the Source 2 light keyvalues to the entity and returns the classname it is spawned as.</summary>
    public static string Apply(BspEntity source, EntityLump.Entity target, Source1EntityConverterOptions options)
    {
        var classname = source.ClassName.ToLowerInvariant();

        target["directlight"] = options.DynamicLights ? 2L : 1L;
        target["renderdiffuse"] = 1L;

        var converted = classname switch
        {
            "light_environment" => ConvertEnvironment(source, target, options),
            "light_spot" => ConvertSpot(source, target, options),
            "light_dynamic" => ConvertDynamic(source, target, options.MinLightingValue),
            "env_projectedtexture" => ConvertProjectedTexture(source, target, options),
            _ => ConvertOmni(source, target, options),
        };

        ApplySwitchableStyle(source, target);

        return converted;
    }

    // A light with an animated style plays its pattern, which the lightmap of the map leaves out. A light with a style of
    // its own is switched by the map, and is dark from the start when it is flagged so.
    private static void ApplySwitchableStyle(BspEntity source, EntityLump.Entity target)
    {
        if (BspLightStyles.TryGetAnimatedLight(source, out _, out var animatedPattern))
        {
            target["pattern"] = animatedPattern;
            return;
        }

        if (!BspLightStyles.TryGetLightStyle(source, out var light))
        {
            return;
        }

        if (light.StartsDisabled)
        {
            target["enabled"] = 0L;
            return;
        }

        var defaultStyle = source.GetInt("defaultstyle");

        if (light.Pattern.Length == 0 && defaultStyle > 0)
        {
            target["pattern"] = BspLightStyles.GetDefaultPattern(defaultStyle);
        }
    }

    private static string ConvertOmni(BspEntity source, EntityLump.Entity target, Source1EntityConverterOptions options)
    {
        WritePointLight(source, target, options);

        return "light_omni";
    }

    private static void WritePointLight(BspEntity source, EntityLump.Entity target, Source1EntityConverterOptions options)
    {
        WriteColor(target, ReadLight(source, "_light", "_lightHDR", "_lightscaleHDR", DefaultSunLight, options.HdrLighting));
        WriteFalloff(source, target);
        WriteBakedLightAngles(source, target, options.TargetOrigins);
        target["castshadows"] = 0L;
    }

    private static string ConvertSpot(BspEntity source, EntityLump.Entity target, Source1EntityConverterOptions options)
    {
        WritePointLight(source, target, options);

        var inner = source.GetFloat("_inner_cone");

        if (inner == 0f)
        {
            inner = DefaultInnerCone;
        }

        var outer = source.GetFloat("_cone");

        if (outer == 0f)
        {
            outer = inner;
        }

        outer = MathF.Max(outer, inner);

        if (inner >= 180f && outer >= 180f)
        {
            return "light_omni";
        }

        WriteCone(target, inner, outer);

        return "light_spot";
    }

    private static void WriteCone(EntityLump.Entity target, float inner, float outer)
    {
        outer = Math.Clamp(outer, 1f, MaxConeAngle);
        inner = Math.Clamp(inner, 0f, MathF.Max(outer - 1f, 0f));

        target["innerconeangle"] = (double)inner;
        target["outerconeangle"] = (double)outer;
    }

    // A dynamic light is an entity of the game and not a light the map compiler bakes. It has a plain
    // 8 bit color that is already linear, a brightness that is an exponent of two, a radius, and is a spot only with
    // an outer cone. The game lights a surface at distance d with min(2, m * r^2 / d^2) * (1 - d^2 / r^2) of its
    // color, where m is the minimum lighting value. That is the inverse square curve of the renderer, which is
    // 1 / d^2 - 1 / r^2 with the range at the radius, scaled so that it is the same 100 units from the light.
    private static string ConvertDynamic(BspEntity source, EntityLump.Entity target, float minLightingValue)
    {
        var exponent = source.GetInt("brightness");
        var radius = source.GetFloat("distance", DefaultDynamicLightDistance);

        WriteDynamicLight(target, ReadDynamicColor(source), exponent, radius, minLightingValue);
        WriteAngles(source, target, isDynamicLight: true);
        target["castshadows"] = 0L;

        var outer = source.GetFloat("_cone");

        if (outer <= 0f)
        {
            return "light_omni";
        }

        WriteCone(target, source.GetFloat("_inner_cone"), outer);

        return "light_spot";
    }

    /// <summary>
    /// Writes the keyvalues of a dynamic light of the game that has no entity in the map, which lights with a plain color
    /// and a radius.
    /// </summary>
    /// <param name="target">The entity that becomes the light.</param>
    /// <param name="color">The color of the light, in 0-255 units.</param>
    /// <param name="radius">How far the light reaches.</param>
    /// <param name="options">The conversion options.</param>
    /// <returns>The classname the entity is spawned as.</returns>
    internal static string ApplyDynamicLight(EntityLump.Entity target, Vector3 color, float radius, Source1EntityConverterOptions options)
    {
        target["directlight"] = options.DynamicLights ? 2L : 1L;
        target["renderdiffuse"] = 1L;

        WriteDynamicLight(target, color, 0, radius, options.MinLightingValue);
        target["castshadows"] = 0L;

        return "light_omni";
    }

    private static void WriteDynamicLight(EntityLump.Entity target, Vector3 linear, int exponent, float radius, float minLightingValue)
    {
        var (color, colorBrightness) = SplitColor(linear);
        var brightnessAtReference = minLightingValue * radius * radius / (ReferenceDistance * ReferenceDistance);

        target["color"] = KVObject.PackedArray([(double)color.X, (double)color.Y, (double)color.Z, 255.0]);
        target["brightness"] = (double)(colorBrightness * MathF.Pow(2f, exponent) * brightnessAtReference);
        target["attenuation0"] = 0.0;
        target["attenuation1"] = 0.0;
        target["attenuation2"] = 1.0;
        target["range"] = (double)radius;
    }

    // A projected texture is a flashlight of the game: a square frustum with the field of view as its angle,
    // that projects a texture and lights a surface at distance d with min(1, 100 / d) of its color, fading out
    // over the last 40 percent of the far plane. The color is a gamma space color scaled by its alpha.
    // It is a barn light, which is the light that projects a texture. A barn light reaches its brightness 100 units
    // from the light, and its perspective frustum starts at the light with a near plane of one unit, so the extent
    // of the frustum at the near plane is the tangent of the half angle of the field of view.
    private static string ConvertProjectedTexture(BspEntity source, EntityLump.Entity target, Source1EntityConverterOptions options)
    {
        const float DefaultFieldOfView = 45f;
        const float DefaultFarZ = 750f;
        const float FarSkirt = 0.4f;
        const float NearPlaneReciprocal = 1f;
        const int StartOnFlag = 1;
        const string DefaultTexture = "effects/flashlight001";

        var (color, brightness) = SplitColor(ReadProjectedColor(source));

        target["color"] = KVObject.PackedArray([(double)color.X, (double)color.Y, (double)color.Z, 255.0]);
        target["brightness"] = (double)MathF.Log2(MathF.Max(brightness, float.Epsilon));

        var farZ = source.GetFloat("farz", DefaultFarZ);

        target["range"] = (double)farZ;
        target["skirt"] = (double)FarSkirt;

        var halfAngle = source.GetFloat("lightfov", DefaultFieldOfView) / 2f;

        WriteCone(target, halfAngle, halfAngle);

        var extent = MathF.Tan(float.DegreesToRadians(Math.Clamp(halfAngle, 1f, MaxConeAngle - 1f))) / NearPlaneReciprocal;

        target["size_params"] = Source1EntityConverter.FormatVector(new Vector3(extent, extent, NearPlaneReciprocal));

        var textureName = source.GetString("texturename", DefaultTexture);
        var texturePath = Source1Paths.ToTexturePath(textureName);
        var frame = source.GetInt("textureframe");

        // A cookie is drawn into a texture array, which needs uncompressed pixels
        texturePath = Source1Paths.AddVariant(texturePath, Source1ResourceProvider.GetCookieVariant(frame));

        target["lightcookie"] = texturePath;
        target["castshadows"] = source.GetInt("enableshadows") != 0 ? 1L : 0L;
        target["directlight"] = 2L;
        target["enabled"] = (source.GetInt("spawnflags") & StartOnFlag) != 0 ? 1L : 0L;

        WriteProjectorAngles(source, target, options.TargetOrigins);

        return "light_barn";
    }

    private static Vector3 ReadProjectedColor(BspEntity source)
    {
        const float DefaultAlpha = 255f;

        var parts = source.GetString("lightcolor").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            return new Vector3(IntensityUnit);
        }

        var channels = source.GetVector3("lightcolor");
        var alpha = DefaultAlpha;

        if (parts.Length >= 4 && BspEntity.TryParseFloat(parts[3], out var parsedAlpha) && parsedAlpha > 0f)
        {
            alpha = parsedAlpha;
        }

        var linear = new Vector3(ToLinear(channels.X), ToLinear(channels.Y), ToLinear(channels.Z));

        return linear * (alpha / IntensityUnit);
    }

    // The projector points along its angles, which are read as they are, or at the entity its target key names
    private static void WriteProjectorAngles(BspEntity source, EntityLump.Entity target, IReadOnlyDictionary<string, Vector3> targetOrigins)
    {
        if (!TryGetTargetAngles(source, targetOrigins, out var angles))
        {
            angles = source.Angles;
        }

        target["angles"] = Source1EntityConverter.FormatVector(angles);
    }

    private static Vector3 ReadDynamicColor(BspEntity source)
    {
        if (!source.Properties.ContainsKey("_light"))
        {
            return new Vector3(IntensityUnit);
        }

        return Vector3.Truncate(source.GetVector3("_light"));
    }

    private static string ConvertEnvironment(BspEntity source, EntityLump.Entity target, Source1EntityConverterOptions options)
    {
        var useHdr = options.HdrLighting;
        var sun = ReadLight(source, "_light", "_lightHDR", "_lightscaleHDR", DefaultSunLight, useHdr);

        WriteColor(target, sun);

        if (!TryReadLight(source, "_ambient", "_ambientHDR", useHdr, out var ambient))
        {
            ambient = sun * 0.5f;
        }

        if (useHdr)
        {
            ambient *= source.GetFloat("_AmbientScaleHDR", 1f);
        }

        var (ambientColor, ambientBrightness) = SplitColor(ambient);

        target["skycolor"] = KVObject.PackedArray([(double)ambientColor.X, (double)ambientColor.Y, (double)ambientColor.Z, 255.0]);
        target["skyintensity"] = (double)ambientBrightness;
        target["angulardiameter"] = 2.0 * source.GetFloat("SunSpreadAngle");
        target["castshadows"] = 1L;
        target["range"] = 0.0;

        WriteBakedLightAngles(source, target, options.TargetOrigins);

        return "light_environment";
    }

    // The light as a linear color in 0-255 units, with the default when the entity has none
    private static Vector3 ReadLight(BspEntity source, string key, string hdrKey, string scaleKey, string defaultValue, bool useHdr)
    {
        if (!TryReadLight(source, key, hdrKey, useHdr, out var linear))
        {
            _ = TryParseLight(defaultValue, useHdr, out linear);
        }

        if (useHdr)
        {
            linear *= source.GetFloat(scaleKey, 1f);
        }

        return linear;
    }

    private static bool TryReadLight(BspEntity source, string key, string hdrKey, bool useHdr, out Vector3 linear)
    {
        if (useHdr && TryParseLight(source.GetString(hdrKey), useHdr, out linear))
        {
            return true;
        }

        return TryParseLight(source.GetString(key), useHdr, out linear);
    }

    private static float ToLinear(float channel)
        => IntensityUnit * MathF.Pow(MathF.Max(channel, 0f) / IntensityUnit, Gamma);

    // A light is one gray value, a color, a color with an intensity, or two of those colors for the LDR and HDR
    // lighting. A negative value marks the light as unset.
    private static bool TryParseLight(string text, bool useHdr, out Vector3 linear)
    {
        linear = default;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var offset = 0;
        var count = parts.Length;

        if (count >= 8)
        {
            offset = useHdr ? 4 : 0;
            count = 4;
        }

        if (count != 1 && count != 3 && count != 4)
        {
            return false;
        }

        Span<float> values = stackalloc float[4];

        for (var i = 0; i < count; i++)
        {
            if (!BspEntity.TryParseFloat(parts[offset + i], out values[i]) || values[i] < 0f)
            {
                return false;
            }
        }

        if (count == 1)
        {
            linear = new Vector3(ToLinear(values[0]));
            return true;
        }

        linear = new Vector3(ToLinear(values[0]), ToLinear(values[1]), ToLinear(values[2]));

        if (count == 4)
        {
            linear *= values[3] / IntensityUnit;
        }

        return true;
    }

    // The color is normalized to its brightest channel in gamma space of 0-255 units, the brightness is what is left
    private static (Vector3 Color, float Brightness) SplitColor(Vector3 linear)
    {
        var peak = MathF.Max(linear.X, MathF.Max(linear.Y, linear.Z));

        if (peak <= 0f)
        {
            return (Vector3.Zero, 0f);
        }

        var color = IntensityUnit * ColorSpace.LinearToGamma22(linear / peak);

        return (color, peak / IntensityUnit);
    }

    private static void WriteColor(EntityLump.Entity target, Vector3 linear)
    {
        var (color, brightness) = SplitColor(linear);

        target["color"] = KVObject.PackedArray([(double)color.X, (double)color.Y, (double)color.Z, 255.0]);
        target["brightness"] = (double)brightness;
    }

    private static void WriteFalloff(BspEntity source, EntityLump.Entity target)
    {
        var brightness = target.GetFloatProperty("brightness");
        var fiftyPercent = source.GetFloat("_fifty_percent_distance");
        var maxDistance = source.GetFloat("_distance");

        float constant;
        float linear;
        float quadratic;
        float range;

        if (fiftyPercent > 0f)
        {
            var zeroPercent = source.GetFloat("_zero_percent_distance");

            if (zeroPercent <= fiftyPercent)
            {
                zeroPercent = 2f * fiftyPercent;
            }

            (constant, linear, quadratic) = SolveFiniteFalloff(fiftyPercent, zeroPercent);

            // The renderer drops negative terms, so a curve that needs one falls back to the inverse square
            // curve that is 1/2 at the fifty percent distance
            if (linear < 0f || quadratic < 0f)
            {
                constant = 1f;
                linear = 0f;
                quadratic = 1f / (fiftyPercent * fiftyPercent);
            }

            // The intensity of the entity is what the light has at the light, not at 100 units
            brightness /= constant + (linear * ReferenceDistance) + (quadratic * ReferenceDistance * ReferenceDistance);

            // The light only ends at the zero percent distance when it is flagged to
            range = source.GetInt("_hardfalloff") != 0 ? zeroPercent : GetReach(constant, linear, quadratic);

            // The renderer zeroes terms below a threshold, which the small terms of a distant light fall under.
            // Scaling all terms keeps the curve, and the renderer divides the brightness by the curve at 100 units
            // on its own, so the light stays as bright.
            ScaleToMinimumTerm(ref constant, ref linear, ref quadratic);
        }
        else
        {
            constant = ClampSmallTerm(source.GetFloat("_constant_attn"));
            linear = ClampSmallTerm(source.GetFloat("_linear_attn"));
            quadratic = ClampSmallTerm(source.GetFloat("_quadratic_attn"));

            if (constant == 0f && linear == 0f && quadratic == 0f)
            {
                constant = 1f;
            }

            range = maxDistance > 0f ? maxDistance : GetReach(constant, linear, quadratic);
        }

        target["brightness"] = (double)brightness;
        target["attenuation0"] = (double)constant;
        target["attenuation1"] = (double)linear;
        target["attenuation2"] = (double)quadratic;
        target["range"] = (double)range;
    }

    // The map compiler fits 1 / (c + l*d + q*d^2) through 1 at the light, 1/2 at the fifty percent distance and 1/256
    // at the zero percent distance. While that curve is not monotonic it lifts the middle point toward the straight
    // line between the end points, then scales the terms so that the curve is exactly 1/2 at the fifty percent distance.
    private static (float Constant, float Linear, float Quadratic) SolveFiniteFalloff(float fiftyPercent, float zeroPercent)
    {
        const float BlendStep = 0.05f;

        var straightMiddle = 1f + ((ReachIntensityRatio - 1f) * fiftyPercent / zeroPercent);
        var determinant = fiftyPercent * zeroPercent * (fiftyPercent - zeroPercent);
        var quadratic = 0f;
        var linear = 0f;

        for (var blend = 0f; blend <= 1f; blend += BlendStep)
        {
            var middle = ((1f - blend) * 2f) + (blend * straightMiddle);

            quadratic = ((zeroPercent * (middle - 1f)) + (fiftyPercent * (1f - ReachIntensityRatio))) / determinant;
            linear = ((zeroPercent * zeroPercent * (1f - middle)) + (fiftyPercent * fiftyPercent * (ReachIntensityRatio - 1f))) / determinant;

            if ((2f * quadratic) + linear >= 0f)
            {
                break;
            }
        }

        var atFiftyPercent = 1f + (fiftyPercent * (linear + (fiftyPercent * quadratic)));
        var scale = 2f / atFiftyPercent;

        return (scale, linear * scale, quadratic * scale);
    }

    private static float ClampSmallTerm(float term)
        => term < MinAttenuationTerm ? 0f : term;

    private static void ScaleToMinimumTerm(ref float constant, ref float linear, ref float quadratic)
    {
        var smallest = float.MaxValue;

        if (constant > 0f)
        {
            smallest = MathF.Min(smallest, constant);
        }

        if (linear > 0f)
        {
            smallest = MathF.Min(smallest, linear);
        }

        if (quadratic > 0f)
        {
            smallest = MathF.Min(smallest, quadratic);
        }

        if (smallest >= MinWrittenAttenuationTerm)
        {
            return;
        }

        var scale = MinWrittenAttenuationTerm / smallest;

        constant *= scale;
        linear *= scale;
        quadratic *= scale;
    }

    // Where the light has fallen to 1/256 of what it is 100 units away, or 0 where it never does
    private static float GetReach(float constant, float linear, float quadratic)
    {
        var scale = constant + (linear * ReferenceDistance) + (quadratic * ReferenceDistance * ReferenceDistance);
        var remaining = (ReachIntensityRatio * scale) - constant;

        if (quadratic > 0f)
        {
            return (-linear + MathF.Sqrt((linear * linear) + (4f * quadratic * remaining))) / (2f * quadratic);
        }

        if (linear > 0f)
        {
            return remaining / linear;
        }

        return 0f;
    }

    // The map compiler points a light at the entity its target key names, and only without one along its angles
    private static void WriteBakedLightAngles(BspEntity source, EntityLump.Entity target, IReadOnlyDictionary<string, Vector3> targetOrigins)
    {
        if (TryGetTargetAngles(source, targetOrigins, out var angles))
        {
            target["angles"] = Source1EntityConverter.FormatVector(angles);
            return;
        }

        WriteAngles(source, target, isDynamicLight: false);
    }

    private static bool TryGetTargetAngles(BspEntity source, IReadOnlyDictionary<string, Vector3> targetOrigins, out Vector3 angles)
    {
        var targetName = source.GetString("target");

        if (targetOrigins.TryGetValue(targetName, out var targetOrigin) && targetOrigin != source.Origin)
        {
            angles = EntityTransformHelper.ForwardDirectionToEulerAngles(targetOrigin - source.Origin);
            return true;
        }

        angles = default;
        return false;
    }

    // The map compiler points a light along its yaw, from the angle key when it has one, and a pitch that is an
    // elevation, from the pitch key when it has one. That is the opposite of the QAngle pitch, which is positive
    // downwards. An angle of -1 points up and -2 points down. The game reads the angles of a dynamic light as they
    // are, and takes an elevation from the pitch key only.
    private static void WriteAngles(BspEntity source, EntityLump.Entity target, bool isDynamicLight)
    {
        var angles = source.Angles;
        var pitch = source.GetFloat("pitch");

        if (isDynamicLight)
        {
            if (pitch != 0f)
            {
                angles.X = -pitch;
            }
        }
        else
        {
            var angle = source.GetFloat("angle");

            if (angle == -1f)
            {
                angles = new Vector3(-MaxConeAngle, 0f, 0f);
            }
            else if (angle == -2f)
            {
                angles = new Vector3(MaxConeAngle, 0f, 0f);
            }
            else
            {
                if (angle != 0f)
                {
                    angles.Y = angle;
                }

                var elevation = pitch != 0f ? pitch : angles.X;
                angles.X = -elevation;
            }
        }

        target["angles"] = Source1EntityConverter.FormatVector(angles);
    }
}
