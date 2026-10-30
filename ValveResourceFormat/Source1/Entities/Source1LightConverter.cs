using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Entities
{
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
        private const int StartDarkFlag = 1;
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
            => classname is "light" or "light_spot" or "light_environment" or "light_dynamic";

        /// <summary>Writes the Source 2 light keyvalues to the entity and returns the classname it is spawned as.</summary>
        public static string Apply(BspEntity source, EntityLump.Entity target, Source1EntityConverterOptions options)
        {
            var classname = source.ClassName.ToLowerInvariant();

            target["directlight"] = options.DynamicLights ? 2L : 1L;
            target["renderdiffuse"] = 1L;

            var converted = classname switch
            {
                "light_environment" => ConvertEnvironment(source, target, options.HdrLighting),
                "light_spot" => ConvertSpot(source, target, options.HdrLighting),
                "light_dynamic" => ConvertDynamic(source, target),
                _ => ConvertOmni(source, target, options.HdrLighting),
            };

            if (classname is "light" or "light_spot")
            {
                ApplySwitchableStyle(source, target);
            }

            return converted;
        }

        // A light with a style of its own is switched by the map, and is dark from the start when it is flagged so
        private static void ApplySwitchableStyle(BspEntity source, EntityLump.Entity target)
        {
            if (source.GetInt("style") < BspLightStyles.FirstSwitchableStyle)
            {
                return;
            }

            if ((source.GetInt("spawnflags") & StartDarkFlag) != 0)
            {
                target["enabled"] = 0L;
                return;
            }

            var defaultStyle = source.GetInt("defaultstyle");

            if (source.GetString("pattern").Length == 0 && defaultStyle > 0)
            {
                target["pattern"] = BspLightStyles.GetDefaultPattern(defaultStyle);
            }
        }

        private static string ConvertOmni(BspEntity source, EntityLump.Entity target, bool useHdr)
        {
            WritePointLight(source, target, useHdr);

            return "light_omni";
        }

        private static void WritePointLight(BspEntity source, EntityLump.Entity target, bool useHdr)
        {
            WriteColor(target, ReadLight(source, "_light", "_lightHDR", "_lightscaleHDR", DefaultSunLight, useHdr));
            WriteFalloff(source, target);
            WriteAngles(source, target, isDynamicLight: false);
            target["castshadows"] = 0L;
        }

        private static string ConvertSpot(BspEntity source, EntityLump.Entity target, bool useHdr)
        {
            WritePointLight(source, target, useHdr);

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
        // 8 bit color, a brightness that is an exponent of two, a radius, and is a spot only with an outer cone.
        private static string ConvertDynamic(BspEntity source, EntityLump.Entity target)
        {
            var color = ReadDynamicColor(source);
            var exponent = source.GetInt("brightness");

            target["color"] = KVObject.PackedArray([(double)color.X, (double)color.Y, (double)color.Z, 255.0]);
            target["brightness"] = (double)MathF.Pow(2f, exponent);

            WriteDynamicLightFalloff(source, target);
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

        private static Vector3 ReadDynamicColor(BspEntity source)
        {
            if (!source.Properties.TryGetValue("_light", out var text))
            {
                return new Vector3(IntensityUnit);
            }

            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return new Vector3(
                ParseColorComponent(parts, 0),
                ParseColorComponent(parts, 1),
                ParseColorComponent(parts, 2));
        }

        private static float ParseColorComponent(string[] parts, int index)
        {
            if (index >= parts.Length || !TryParseFloat(parts[index], out var value))
            {
                return 0f;
            }

            return MathF.Truncate(value);
        }

        private static string ConvertEnvironment(BspEntity source, EntityLump.Entity target, bool useHdr)
        {
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

            WriteAngles(source, target, isDynamicLight: false);

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
                if (!TryParseFloat(parts[offset + i], out values[i]) || values[i] < 0f)
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

        private static bool TryParseFloat(string text, out float value)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        // The color is normalized to its brightest channel in gamma space of 0-255 units, the brightness is what is left
        private static (Vector3 Color, float Brightness) SplitColor(Vector3 linear)
        {
            var peak = MathF.Max(linear.X, MathF.Max(linear.Y, linear.Z));

            if (peak <= 0f)
            {
                return (Vector3.Zero, 0f);
            }

            var color = IntensityUnit * ColorSpace.SrgbLinearToGamma(linear / peak);

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

                // 1 / (1 + l*d + q*d^2) is 1/2 at the fifty percent distance and 1/256 at the zero percent distance
                var determinant = (fiftyPercent * zeroPercent * zeroPercent) - (fiftyPercent * fiftyPercent * zeroPercent);
                constant = 1f;
                linear = ((zeroPercent * zeroPercent) - (ReachIntensityRatio - 1f) * fiftyPercent * fiftyPercent) / determinant;
                quadratic = ((ReachIntensityRatio - 1f) * fiftyPercent - zeroPercent) / determinant;

                // The renderer drops negative terms, so a curve that needs one falls back to the inverse square
                // curve that is 1/2 at the fifty percent distance
                if (linear < 0f || quadratic < 0f)
                {
                    linear = 0f;
                    quadratic = 1f / (fiftyPercent * fiftyPercent);
                }

                // The intensity of the entity is what the light has at the light, not at 100 units
                brightness /= constant + (linear * ReferenceDistance) + (quadratic * ReferenceDistance * ReferenceDistance);
                range = zeroPercent;

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

        private static void WriteDynamicLightFalloff(BspEntity source, EntityLump.Entity target)
        {
            var distance = source.GetFloat("distance", DefaultDynamicLightDistance);

            target["attenuation0"] = 1.0;
            target["attenuation1"] = 0.0;
            target["attenuation2"] = 0.0;
            target["range"] = (double)distance;
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

            target["angles"] = string.Create(CultureInfo.InvariantCulture, $"{angles.X} {angles.Y} {angles.Z}");
        }
    }
}
