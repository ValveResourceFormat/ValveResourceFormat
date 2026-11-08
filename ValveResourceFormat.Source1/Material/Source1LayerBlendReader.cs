using System.Globalization;
using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Describes what the Source 1 material says about the blending of its layers, the detail textures of its layers,
/// the surface properties of its layers and the effects of unlit surfaces.
/// </summary>
internal static class Source1LayerBlendReader
{
    private const int DetailBlendModeSelectPattern = 7;

    /// <summary>Describes the layers and effects of the material.</summary>
    public static void Read(MaterialDescription description, ResourceTypes.Material material, VmtParameters values)
    {
        ReadSurfaceProperties(description, values);

        if (description.Role is MaterialRole.LitWorld or MaterialRole.Blend2)
        {
            description.LayerBlending = ReadLayerBlending(description, material, values);
        }

        if (description.Role == MaterialRole.Unlit)
        {
            ReadUnlit(description, material, values);
        }
    }

    // Each surface property needs the one before it
    private static void ReadSurfaceProperties(MaterialDescription description, VmtParameters values)
    {
        if (description.SurfaceProperty is null)
        {
            return;
        }

        for (var layer = 2; layer <= 4; layer++)
        {
            var key = "$surfaceprop" + layer.ToString(CultureInfo.InvariantCulture);

            if (values.String(key)?.ToLowerInvariant() is not { } surfaceProperty)
            {
                return;
            }

            description.LayerSurfaceProperties.Add(surfaceProperty);
        }
    }

    private static MaterialLayerBlending ReadLayerBlending(MaterialDescription description, ResourceTypes.Material material, VmtParameters values)
    {
        var blending = new MaterialLayerBlending();
        var isBlend2 = description.Role == MaterialRole.Blend2;
        var color = values.Vector("$color", Vector3.One);

        if (isBlend2)
        {
            blending.Tints.Add(color * values.Vector("$layertint1", Vector3.One));
            blending.Tints.Add(color * values.Vector("$layertint2", Vector3.One));
        }
        else
        {
            blending.Tints.Add(color);
        }

        if (isBlend2 && values.Integer("$newlayerblending") > 0)
        {
            blending.Border = new MaterialBorderBlend
            {
                Tint = values.Vector("$layerbordertint", Vector3.One),
                Softness = values.Float("$blendsoftness", 0.5f),
                BorderStrength = values.Float("$layerborderstrength", 0.5f),
                BorderOffset = values.Float("$layerborderoffset"),
                BorderSoftness = values.Float("$layerbordersoftness", 0.5f),
            };
        }

        ReadDetails(blending, material, values, isBlend2);

        return blending;
    }

    private static void ReadDetails(MaterialLayerBlending blending, ResourceTypes.Material material, VmtParameters values, bool isBlend2)
    {
        var textures = material.TextureParams;

        if (material.IntParams.GetValueOrDefault("F_DETAIL") == 0 || !textures.TryGetValue("g_tDetail", out var detail))
        {
            return;
        }

        blending.Details.Add(CreateDetail(detail, values, "$detailscale", "$detailtint", "$detailblendfactor"));

        if (isBlend2 && textures.TryGetValue("g_tDetail2", out var detail2))
        {
            blending.Details.Add(CreateDetail(detail2, values, "$detailscale2", "$detailtint2", "$detailblendfactor2"));
            blending.SelectDetailPattern = values.Integer("$detailblendmode") == DetailBlendModeSelectPattern;
        }

        // The detail scales of the normal maps replace the first two components of the tint of the first detail
        var hasNormals = textures.ContainsKey("g_tNormal") && textures.ContainsKey("g_tNormal2");

        if (isBlend2 && hasNormals && values.Integer("$addbumpmaps") != 0)
        {
            blending.BumpDetailScale = new Vector2(values.Float("$bumpdetailscale1", 1f), values.Float("$bumpdetailscale2", 1f));
        }
    }

    private static MaterialLayerDetail CreateDetail(string texture, VmtParameters values, string scaleKey, string tintKey, string blendKey)
    {
        return new MaterialLayerDetail
        {
            Texture = texture,
            Scale = values.Pair(scaleKey, new Vector2(values.Float(scaleKey, 4f))),
            Tint = values.Vector(tintKey, Vector3.One),
            BlendFactor = values.Float(blendKey, 1f),
        };
    }

    private static void ReadUnlit(MaterialDescription description, ResourceTypes.Material material, VmtParameters values)
    {
        description.DisableDepth = values.Boolean("$ignorez");

        if (values.Boolean("$multiply"))
        {
            description.BlendMode = MaterialBlendMode.Multiply;
        }

        if (values.Boolean("$gradientmodulation"))
        {
            var white = Vector4.One;

            description.GradientStops =
            [
                values.Color4("$gradientcolorstop0", white),
                values.Color4("$gradientcolorstop1", white),
                values.Color4("$gradientcolorstop2", white),
            ];
        }

        if (values.Boolean("$beachfoam"))
        {
            description.Effect = MaterialEffect.BeachFoam;
        }
        else if (values.Boolean("$moondome"))
        {
            description.Effect = MaterialEffect.MoonDome;
            description.CubeParallax = values.Float("$cubeparallax");

            if (material.TextureParams.TryGetValue("g_tColor2", out var cube))
            {
                description.CubeMap = MaterialInput.FromTexture(cube, Channel.RGB);
            }
        }
    }
}
