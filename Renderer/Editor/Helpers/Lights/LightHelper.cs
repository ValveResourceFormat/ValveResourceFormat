using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The light an entity casts, as its keys describe it, such as the light of the sun. It draws nothing while
/// its entity is selected.
/// </summary>
internal sealed class LightHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override GizmoTransformModes SupportedTransformModes => GizmoTransformModes.Translate | GizmoTransformModes.Rotate;

    /// <inheritdoc/>
    public override void AddToScene()
    {
        AddLight(Owner);
    }

    /// <summary>Adds the light an entity casts to its scene.</summary>
    /// <param name="owner">The entity.</param>
    /// <returns>
    /// The light, or <see langword="null"/> when the class of the entity casts none or the light casts no direct
    /// light.
    /// </returns>
    internal static SceneLight? AddLight(IHelperOwner owner)
    {
        var (accepted, type) = SceneLight.IsAccepted(owner.Classname);

        if (!accepted)
        {
            return null;
        }

        var scene = owner.Scene;
        var keyValues = owner.KeyValues;
        var light = SceneLight.FromEntityProperties(scene, type, keyValues);

        if (!PreviewInRealTime(light, keyValues, scene))
        {
            return null;
        }

        if (light.IsLight2)
        {
            light.LinearBrightness = GetLinearBrightness(light, keyValues);
        }

        light.Flags |= ObjectTypeFlags.NoShadows;
        light.PlaceAt(owner.Transform);
        owner.AddNode(light, followsEntity: true);

        return light;
    }

    // Nothing of a source map is baked, so every light is shown as it looks lit in real time. A light that is
    // off stays in the scene without brightness.
    private static bool PreviewInRealTime(SceneLight light, Entity keyValues, Scene scene)
    {
        var directLight = light.DirectLight;

        // A game that bakes no direct light lights everything in real time, an older light even with direct light off
        if (!scene.RendererContext.FileLoader.GetGameInfoBool("WorldRenderer/DirectLightBaking", true)
            && (directLight != SceneLight.DirectLightType.None || !light.IsLight2))
        {
            directLight = SceneLight.DirectLightType.Dynamic;
        }

        if (directLight == SceneLight.DirectLightType.None)
        {
            return false;
        }

        // A barn, rect or omni2 light that is only baked lights the map whether or not it is on
        var isLit = light.Enabled || (light.IsLight2 && directLight == SceneLight.DirectLightType.Static);

        if (directLight != SceneLight.DirectLightType.Dynamic)
        {
            light.ShadowMapSize = GetBakedPreviewShadowSize(light, keyValues, directLight);
        }

        light.DirectLight = SceneLight.DirectLightType.Dynamic;
        light.StationaryLightIndex = -1;
        light.Enabled = true;

        if (!isLit)
        {
            light.BrightnessScale = 0f;
        }

        return true;
    }

    // A baked light is previewed with a small shadow map
    private static int GetBakedPreviewShadowSize(SceneLight light, Entity keyValues, SceneLight.DirectLightType directLight)
    {
        const int MaxSize = 512;

        if (light.IsLight2)
        {
            var size = keyValues.GetInt32Property("shadowmapsize");

            return directLight == SceneLight.DirectLightType.Stationary && size > 0 ? size : MaxSize;
        }

        var fallback = light.Entity is SceneLight.EntityType.Omni or SceneLight.EntityType.Capsule ? MaxSize / 2 : MaxSize;
        var width = keyValues.GetInt32Property("shadowtexturewidth");
        var height = keyValues.GetInt32Property("shadowtextureheight");

        // An orthographic light's shadow map is as high as its width keeps it in proportion
        if (light.Entity == SceneLight.EntityType.Ortho)
        {
            width = width > 0 ? width : 2048;
            height = Math.Clamp((int)(width * light.SizeParams.Y / light.SizeParams.X), 1, 16384);
        }

        width = width > 0 ? width : fallback;
        height = height > 0 ? height : fallback;

        while (Math.Max(width, height) > MaxSize)
        {
            width = Math.Max(width / 2, 1);
            height = Math.Max(height / 2, 1);
        }

        return Math.Max(width, height);
    }

    /// <summary>
    /// Gets the brightness of a barn, rect or omni2 light from the key of the unit it is set in, as its intensity
    /// 100 units in front of it: lumens are spread over the solid angle it lights, and nits over the area of its
    /// luminaire too.
    /// </summary>
    private static float GetLinearBrightness(SceneLight light, Entity keyValues)
    {
        const float ReferenceDistanceSq = 10000f;
        const float NitsPerUnitArea = 0.00064516f;
        const float SolidAngleScale = 0.0079577472f;

        var exposure = float.Exp2(keyValues.GetFloatProperty("brightness"));
        var unit = keyValues.GetInt32Property("brightness_units");

        // The other units of a barn light depend on the solid angle of its frustum, and the exposure value is kept
        // in step with them
        if (light.Entity == SceneLight.EntityType.Barn)
        {
            return unit == 3 ? keyValues.GetFloatProperty("brightness_legacy") : exposure;
        }

        float solidAngle;
        float area;

        if (light.Entity == SceneLight.EntityType.Rect)
        {
            solidAngle = MathF.PI;
            area = light.Shape == 1f
                ? MathF.PI * light.SizeParams.X * light.SizeParams.Y
                : 4f * light.SizeParams.X * light.SizeParams.Y;
        }
        else
        {
            var cosOuter = MathF.Cos(float.DegreesToRadians(light.SpotOuterAngle));
            var cosInner = MathF.Cos(float.DegreesToRadians(light.SpotInnerAngle));

            solidAngle = (1f - (cosOuter + cosInner) * 0.5f) * MathF.Tau;

            var (radius, halfLength) = (light.SizeParams.X, light.SizeParams.Y);

            area = light.LuminaireShape switch
            {
                1 => 2f * MathF.Tau * radius * halfLength,
                2 => 2f * MathF.Tau * radius * halfLength + MathF.Tau * radius * radius,
                _ => 2f * MathF.Tau * radius * radius,
            };
        }

        var spread = solidAngle * SolidAngleScale * ReferenceDistanceSq;

        return unit switch
        {
            0 => exposure,
            1 => keyValues.GetFloatProperty("brightness_lumens") / spread,
            2 => keyValues.GetFloatProperty("brightness_nits") * area * MathF.PI * NitsPerUnitArea / spread,
            3 => keyValues.GetFloatProperty("brightness_legacy"),
            4 => keyValues.GetFloatProperty("brightness_candelas") * solidAngle / spread,
            _ => 0f,
        };
    }
}
