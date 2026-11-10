using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>ent_dota_lightinfo</c>: lighting, fog and sky of the area around it. The game follows the camera: the
/// first light info whose inner radius holds it wins outright, otherwise the ones around it blend. A viewer
/// has no camera at load, so the <see cref="EnvGlobalLight"/> stands in for it, and takes the sun, ambient
/// light, fog and sky from the <see cref="Blend"/> picked there.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/dota2/client/C_DOTA_LightInfo">C_DOTA_LightInfo</seealso>
public sealed class DotaLightInfo : BaseEntity
{
    /// <summary>The light infos in effect at a position, and how much each one counts.</summary>
    internal sealed class Blend
    {
        private readonly List<(DotaLightInfo LightInfo, float Weight)> members;

        /// <summary>Gets the light info that counts most, which gives what cannot blend, like the sky.</summary>
        private DotaLightInfo Dominant { get; }

        /// <summary>Initializes the blend of light infos whose weights add up to one.</summary>
        public Blend(List<(DotaLightInfo LightInfo, float Weight)> members)
        {
            this.members = members;

            var dominant = members[0];

            foreach (var member in members)
            {
                if (member.Weight > dominant.Weight)
                {
                    dominant = member;
                }
            }

            Dominant = dominant.LightInfo;
        }

        /// <summary>
        /// Sets the sun and ambient light of a light. The light infos give <c>light_direction</c> as the
        /// direction of the sun, and the shadow colors and scales as the sky and ground ambient light.
        /// </summary>
        /// <param name="sun">The directional light, which keeps its position.</param>
        /// <param name="ambientScale">What the ambient intensities are multiplied by.</param>
        public void ApplyTo(SceneLight sun, float ambientScale)
        {
            var sunDirection = GetDirection("light_direction_day");
            var sunTransform = EntityTransformHelper.ForwardDirectionToRotationMatrix(sunDirection) with
            {
                Translation = sun.Position
            };

            sun.PlaceAt(sunTransform);
            sun.Color = GetColor("color_day");
            sun.Brightness = GetFloat("light_scale_day");
            sun.SkyColor = GetColor("shadow_color_day");
            sun.GroundColor = GetColor("shadow_secondary_color_day");
            sun.SkyIntensity = GetFloat("shadow_ground_scale_day") * ambientScale;
            sun.AmbientDirectionColor = GetColor("ambient_color_day");
            sun.AmbientDirectionIntensity = GetFloat("ambient_scale_day") * ambientScale;
            sun.AmbientDirection = GetDirection("ambient_direction_day");
            sun.AmbientShadowAmount = GetFloat("ambient_shadow_amount");
            sun.SpecularDirection = GetDirection("specular_direction_day");
            sun.SpecularColor = GetColor("specular_color_day");
            sun.SpecularPower = GetSpecularPower(sun.SpecularPower);
            sun.SpecularIndependence = GetFloat("specular_independence_day");
        }

        /// <summary>Replaces the color and distances of the fog of a scene, and leaves its density and exponent.</summary>
        public void ApplyFog(Scene scene)
        {
            var start = GetFloat("fog_start_day");
            var end = GetFloat("fog_end_day");
            if (start == 0f && end == 0f)
            {
                return;
            }

            var color = ColorSpace.SrgbGammaToLinear(GetGammaColor("fog_color_day"));
            var fog = scene.FogInfo.LinearFog ?? LinearFog.None;

            scene.FogInfo.LinearFog = fog with { Color = color, Start = start, End = end };
        }

        /// <summary>Replaces the 2D skybox of a scene with the one of the dominant light info.</summary>
        public void ApplySkybox(Scene scene)
        {
            var keyValues = Dominant.KeyValues;
            var materialName = keyValues.GetStringProperty("skybox_material_day");
            if (materialName == null)
            {
                return;
            }

            var tint = ColorSpace.SrgbGammaToLinear(keyValues.GetColor32Property("skybox_tint_color_day"));
            var transform = EntityTransformHelper.EulerAnglesToRotationMatrix(keyValues.GetVector3Property("skybox_angle_day"));
            var fogType = (SkyFogType)keyValues.GetInt32Property("skybox_fog_type");
            var angularFog = new Vector4(
                keyValues.GetFloatProperty("skybox_angular_fog_min_start"),
                keyValues.GetFloatProperty("skybox_angular_fog_min_end"),
                keyValues.GetFloatProperty("skybox_angular_fog_max_start"),
                keyValues.GetFloatProperty("skybox_angular_fog_max_end"));

            SceneSkybox2D.Replace(scene, materialName, tint, transform, fogType, angularFog);
        }

        /// <summary>Gets the weighted sum of a number.</summary>
        private float GetFloat(string key)
        {
            var sum = 0f;

            foreach (var (lightInfo, weight) in members)
            {
                sum += weight * lightInfo.KeyValues.GetFloatProperty(key);
            }

            return sum;
        }

        /// <summary>
        /// Gets the weighted sum of the specular power. A light info that leaves it at minus one for either time
        /// of day keeps the power the light already has.
        /// </summary>
        private float GetSpecularPower(float lightPower)
        {
            var sum = 0f;

            foreach (var (lightInfo, weight) in members)
            {
                var keyValues = lightInfo.KeyValues;
                var dayPower = keyValues.GetFloatProperty("specular_power_day");
                var nightPower = keyValues.GetFloatProperty("specular_power_night");
                var power = dayPower == -1f || nightPower == -1f ? lightPower : dayPower;

                sum += weight * power;
            }

            return sum;
        }

        /// <summary>Gets the weighted sum of a color in sRGB, which is not converted before it is summed.</summary>
        private Vector3 GetGammaColor(string key)
        {
            var sum = Vector3.Zero;

            foreach (var (lightInfo, weight) in members)
            {
                sum += weight * lightInfo.KeyValues.GetColor32Property(key);
            }

            return sum;
        }

        /// <summary>Gets the weighted sum of a color, summed in linear space and given in sRGB.</summary>
        private Vector3 GetColor(string key)
        {
            var sum = Vector3.Zero;

            foreach (var (lightInfo, weight) in members)
            {
                sum += weight * ColorSpace.SrgbGammaToLinear(lightInfo.KeyValues.GetColor32Property(key));
            }

            return ColorSpace.SrgbLinearToGamma(sum);
        }

        /// <summary>Gets the weighted sum of the directions that angles point in, as a direction.</summary>
        private Vector3 GetDirection(string anglesKey)
        {
            var sum = Vector3.Zero;

            foreach (var (lightInfo, weight) in members)
            {
                var angles = lightInfo.KeyValues.GetVector3Property(anglesKey);

                sum += weight * EntityTransformHelper.EulerAnglesToForwardDirection(angles);
            }

            return MathUtils.SafeNormalize(sum);
        }
    }

    /// <summary>Initializes an <c>ent_dota_lightinfo</c> from its keyvalues.</summary>
    public DotaLightInfo(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    internal static Blend? Select(IEnumerable<DotaLightInfo> lightInfos, Vector3 position)
    {
        var partial = new List<(DotaLightInfo LightInfo, float Weight)>();

        foreach (var lightInfo in lightInfos)
        {
            var keyValues = lightInfo.KeyValues;

            // Other weathers are left out, as the game does in clear weather
            if (keyValues.GetInt32Property("weather_type") is not (0 or -1))
            {
                continue;
            }

            var offset = lightInfo.WorldOrigin - position;
            var distance = offset.AsVector2().Length();
            var innerRadius = keyValues.GetFloatProperty("inner_radius");
            var outerRadius = keyValues.GetFloatProperty("outer_radius");

            if (distance >= outerRadius)
            {
                continue;
            }

            if (distance < innerRadius)
            {
                return new Blend([(lightInfo, 1f)]);
            }

            var weight = 1f - (distance - innerRadius) / (outerRadius - innerRadius);
            partial.Add((lightInfo, weight));
        }

        if (partial.Count == 0)
        {
            return null;
        }

        // Each one counts for what the others leave of it
        var weights = new float[partial.Count];
        var total = 0f;

        for (var i = 0; i < partial.Count; i++)
        {
            var weight = partial[i].Weight;

            for (var j = 0; j < partial.Count; j++)
            {
                if (j != i)
                {
                    weight *= 1f - partial[j].Weight;
                }
            }

            weights[i] = weight;
            total += weight;
        }

        if (total <= 0f)
        {
            return new Blend([(partial[0].LightInfo, 1f)]);
        }

        var members = new List<(DotaLightInfo LightInfo, float Weight)>(partial.Count);

        for (var i = 0; i < partial.Count; i++)
        {
            members.Add((partial[i].LightInfo, weights[i] / total));
        }

        return new Blend(members);
    }
}
