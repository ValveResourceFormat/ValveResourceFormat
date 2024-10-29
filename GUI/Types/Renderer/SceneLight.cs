using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer;

class SceneLight(Scene scene) : SceneNode(scene)
{
    /// <summary>
    /// Light index to a baked lightmap.
    /// Range: 0..255 for GameLightmapVersion 1 and 0..3 for GameLightmapVersion 2.
    /// </summary>
    public int StationaryLightIndex { get; set; }

    public enum LightType
    {
        Directional,
        Point,
        Spot,
    }

    public enum EntityType
    {
        Environment,
        Omni,
        Spot,
        Omni2,
        Barn,
        Rect,
    }

    public Vector3 Position { get; set; }
    public Vector3 Color { get; set; } = Vector3.One;
    public float Brightness { get; set; } = 1.0f;
    public float FallOff { get; set; } = 1.0f;
    public float SpotInnerAngle { get; set; }
    public float SpotOuterAngle { get; set; } = 45.0f;
    public float AttenuationLinear { get; set; }
    public float AttenuationQuadratic { get; set; }
    public LightType Type { get; set; }
    public EntityType Entity { get; set; }

    public static (bool Accepted, EntityType Type) IsAccepted(string classname)
    {
        if (!classname.StartsWith("light_", StringComparison.OrdinalIgnoreCase))
        {
            return (false, EntityType.Environment);
        }

        var accepted = Enum.TryParse(classname[6..], true, out EntityType entityType);
        return (accepted, entityType);
    }

    public Vector3 Direction { get; set; }
    public float Range { get; set; } = 512.0f;

    public static SceneLight FromEntityProperties(Scene scene, EntityType type, KVObject entity)
    {
        var light = new SceneLight(scene)
        {
            StationaryLightIndex = entity.GetPropertyUnchecked("bakedshadowindex", entity.GetPropertyUnchecked("bakelightindex", -1)),
            Entity = type,
            Type = type switch
            {
                EntityType.Environment => LightType.Directional,

                EntityType.Omni => LightType.Point,
                EntityType.Omni2 => LightType.Point,

                EntityType.Barn => LightType.Spot,
                EntityType.Rect => LightType.Spot,
                EntityType.Spot => LightType.Spot,
                _ => throw new NotImplementedException()
            },

            Color = entity.GetVector3Property("color") / 255.0f,

            Brightness = type switch
            {
                EntityType.Environment or EntityType.Omni or EntityType.Spot => entity.GetPropertyUnchecked("brightness", 1.0f),
                _ => entity.GetPropertyUnchecked("brightness_legacy", 1.0f),
            },

            Range = entity.GetPropertyUnchecked("range", 512.0f),
            FallOff = entity.GetPropertyUnchecked("skirt", 0.1f),
        };

        var isNewLightType = type is EntityType.Omni2 or EntityType.Barn or EntityType.Rect;

        if (!isNewLightType)
        {
            light.AttenuationLinear = entity.GetPropertyUnchecked("attenuation1", 0.0f);
            light.AttenuationQuadratic = entity.GetPropertyUnchecked("attenuation2", 0.0f);
        }

        if (type is EntityType.Spot)
        {
            light.SpotInnerAngle = entity.GetPropertyUnchecked("innerconeangle", light.SpotInnerAngle);
            light.SpotOuterAngle = entity.GetPropertyUnchecked("outerconeangle", light.SpotOuterAngle);
        }

        var angles = EntityTransformHelper.GetPitchYawRoll(entity);

        light.Position = EntityTransformHelper.ParseVector(entity.GetProperty<string>("origin"));
        light.Direction = new Vector3(
            MathF.Cos(angles.Y) * MathF.Cos(angles.X),
            MathF.Sin(angles.Y) * MathF.Cos(angles.X),
            MathF.Sin(angles.X)
        );

        light.Direction = Vector3.Normalize(light.Direction);

        return light;
    }

    public override void Update(Scene.UpdateContext context)
    {
        //throw new NotImplementedException();
    }

    public override void Render(Scene.RenderContext context)
    {
        //throw new NotImplementedException();
    }
}
