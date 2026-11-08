using System.Globalization;

namespace ValveResourceFormat.Source1.Material;

internal sealed partial class MaterialWriter
{
    private void WriteLayerBlending()
    {
        if (!profile.LayerBlending || description.LayerBlending is not { } blending)
        {
            return;
        }

        for (var i = 0; i < blending.Tints.Count && i < profile.Layers; i++)
        {
            if (blending.Tints[i] != Vector3.One)
            {
                var number = (i + 1).ToString(CultureInfo.InvariantCulture);
                SetVector($"g_vLayer{number}Tint", new Vector4(blending.Tints[i], 0f));
            }
        }

        if (blending.Border is { } border && profile.Layers > 1)
        {
            SetFeature("F_FANCY_BLENDING", 2);
            SetVector("g_vLayerBorderTint", new Vector4(border.Tint, 0f));
            SetFloat("g_flBlendSoftness", "blend softness", border.Softness);
            SetFloat("g_flLayerBorderStrength", "layer border strength", border.BorderStrength);
            SetFloat("g_flLayerBorderOffset", "layer border offset", border.BorderOffset);
            SetFloat("g_flLayerBorderSoftness", "layer border softness", border.BorderSoftness);
        }

        WriteLayerDetails(blending);

        // The list holds the properties of the layers after the first
        for (var i = 0; i < description.LayerSurfaceProperties.Count && i < profile.Layers - 1; i++)
        {
            translation.LayerSurfaceProperties.Add(description.LayerSurfaceProperties[i]);
        }
    }

    private void WriteLayerDetails(MaterialLayerBlending blending)
    {
        var detailCount = 0;

        for (var i = 0; i < blending.Details.Count && i < profile.Layers; i++)
        {
            var detail = blending.Details[i];
            var number = (i + 1).ToString(CultureInfo.InvariantCulture);
            var texture = MaterialInput.FromTexture(detail.Texture, ValveResourceFormat.CompiledShader.ChannelMapping.RGB);

            if (!AddTexture(MaterialSlots.LayerDetail, $"TextureLayer{number}Detail", texture, "_detail"))
            {
                break;
            }

            detailCount++;
            SetVector($"g_vLayer{number}DetailScale", new Vector4(detail.Scale, 0f, 0f));

            var tintAndBlend = new Vector4(detail.Tint, detail.BlendFactor);

            if (i == 0 && blending.BumpDetailScale is { } bumpScale)
            {
                tintAndBlend.X = bumpScale.X;
                tintAndBlend.Y = bumpScale.Y;
            }

            SetVector($"g_vLayer{number}DetailTintAndBlend", tintAndBlend);
        }

        if (detailCount == 0)
        {
            return;
        }

        SetFeature("F_DETAILTEXTURE", detailCount);

        if (detailCount == 2 && blending.SelectDetailPattern)
        {
            SetFeature("F_DETAILBLENDMODE", 1);
        }

        if (blending.BumpDetailScale is not null)
        {
            SetFeature("F_ADDBUMPMAPS", 1);
        }
    }

    private void WriteEffects()
    {
        if (description.DisableDepth)
        {
            SetFeature(profile.DepthFeature, 1);
        }

        if (description.GradientStops is { } stops && profile.GradientFeature is not null)
        {
            SetFeature(profile.GradientFeature, 1);

            for (var i = 0; i < stops.Length; i++)
            {
                var number = i.ToString(CultureInfo.InvariantCulture);
                SetVector($"g_vGradientColorStop{number}", new Vector4(stops[i].X, stops[i].Y, stops[i].Z, 0f));
            }

            SetVector("g_vGradientModulation_StopPositions", new Vector4(stops[0].W, stops[1].W, stops[2].W, 0f));
        }

        if (AddTexture(MaterialSlots.CubeMap, "TextureCubeMap", description.CubeMap, "_cube"))
        {
            SetFloat(profile.CubeParallaxParam, "cube map parallax", description.CubeParallax);
        }
    }
}
