namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Converts Source 1 materials to Source 2 style material resources.
/// </summary>
internal static class Source1MaterialConverter
{
    public const string ShaderPrefix = "source1_";

    private static readonly (string Key, string Parameter)[] TextureMap =
    [
        ("$basetexture", "g_tColor"),
        ("$bumpmap", "g_tNormal"),
        ("$basetexture2", "g_tColor2"),
        ("$bumpmap2", "g_tNormal2"),
        ("$envmapmask", "g_tEnvMapMask"),
        ("$selfillummask", "g_tSelfIllumMask"),
        ("$detail", "g_tDetail"),
        ("$lightwarptexture", "g_tLightWarp"),
        ("$phongexponenttexture", "g_tPhongExponent"),
        ("$phongwarptexture", "g_tPhongWarp"),
        ("$blendmodulatetexture", "g_tBlendModulate"),
        ("$iris", "g_tIris"),
        ("$corneatexture", "g_tCornea"),
        ("$ambientoccltexture", "g_tAmbientOccl"),
    ];

    /// <summary>
    /// Creates a material resource from a parsed VMT. The Source 1 shader name is kept, lowercase, with the <see cref="ShaderPrefix"/>.
    /// </summary>
    /// <param name="vmt">The parsed material.</param>
    /// <param name="name">The name of the material resource.</param>
    /// <param name="baseTextureHasAlpha">Whether the base texture has an alpha channel. Without one, its alpha cannot drive self illumination or the environment map mask.</param>
    public static ResourceTypes.Material ToVrfMaterial(VmtFile vmt, string? name = null, bool baseTextureHasAlpha = true)
    {
        ArgumentNullException.ThrowIfNull(vmt);

        var values = vmt.Values;
        var material = new ResourceTypes.Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = name ?? string.Empty,
            ShaderName = ShaderPrefix + vmt.NormalizedShaderName,
        };

        var shader = vmt.NormalizedShaderName;
        var isWater = shader is "water" or "refract" or "watercheap";
        var isLightmapped = shader is "lightmappedgeneric" or "worldvertextransition" or "lightmappedreflective" or "lightmappedtwotexture";
        var isVertexLit = shader is "vertexlitgeneric" or "eyes" or "eyerefract" or "teeth" or "skin";
        var isUnlit = shader is "unlitgeneric";

        // The ripples of water come from the normal map, a $bumpmap of water is the du/dv map of older hardware
        if (isWater && values.String("$normalmap") is { } normalMap && !string.IsNullOrWhiteSpace(normalMap))
        {
            material.TextureParams["g_tNormal"] = VmtFile.NormalizePath(normalMap, ".vtf");
        }

        foreach (var (key, parameter) in TextureMap)
        {
            var path = values.String(key);

            if (string.IsNullOrWhiteSpace(path) || material.TextureParams.ContainsKey(parameter))
            {
                continue;
            }

            if (isWater && key == "$bumpmap")
            {
                continue;
            }

            material.TextureParams[parameter] = VmtFile.NormalizePath(path, ".vtf");
        }

        var hasBaseTexture = material.TextureParams.ContainsKey("g_tColor");
        var hasBump = material.TextureParams.ContainsKey("g_tNormal");
        var hasLightWarp = material.TextureParams.ContainsKey("g_tLightWarp");
        var hasDetail = material.TextureParams.ContainsKey("g_tDetail");
        var hasEnvmapMask = material.TextureParams.ContainsKey("g_tEnvMapMask");
        var hasSelfIllumMask = material.TextureParams.ContainsKey("g_tSelfIllumMask");

        // Phong needs a bump map, the base alpha as its mask or a light warp texture
        var basemapAlphaPhongMask = values.Boolean("$basemapalphaphongmask");
        var phong = values.Boolean("$phong") && (hasBump || basemapAlphaPhongMask || hasLightWarp);

        var envmap = values.String("$envmap");
        var hasEnvmap = !string.IsNullOrWhiteSpace(envmap);

        var selfIllum = values.Boolean("$selfillum");
        var wantsSelfIllumFresnel = values.Boolean("$selfillumfresnel");
        var baseAlphaEnvmapMask = values.Boolean("$basealphaenvmapmask");
        var normalMapAlphaEnvmapMask = values.Boolean("$normalmapalphaenvmapmask");

        if (!hasBaseTexture)
        {
            selfIllum = false;
            baseAlphaEnvmapMask = false;
        }
        else if (!baseTextureHasAlpha)
        {
            // A vertex lit model can still glow from its own mask texture or a fresnel term
            selfIllum = selfIllum && isVertexLit && (hasSelfIllumMask || wantsSelfIllumFresnel);
            baseAlphaEnvmapMask = false;
        }

        if (!isVertexLit && !isLightmapped)
        {
            selfIllum = false;
        }

        if (isVertexLit && !phong)
        {
            if (!hasBump && !hasLightWarp)
            {
                normalMapAlphaEnvmapMask = false;
            }

            if (normalMapAlphaEnvmapMask)
            {
                hasEnvmapMask = false;
                baseAlphaEnvmapMask = false;
            }

            // A bump mapped model can only mask the environment map with the alpha of its normal map
            if (hasBump && baseAlphaEnvmapMask && !normalMapAlphaEnvmapMask)
            {
                hasEnvmap = false;
            }

            if (hasBump && hasEnvmapMask)
            {
                hasEnvmapMask = false;

                if (!normalMapAlphaEnvmapMask)
                {
                    hasEnvmap = false;
                }
            }
        }

        if (isLightmapped && hasBump)
        {
            hasEnvmapMask = false;
        }

        // An unlit surface has no normal map to take a mask from
        if (isUnlit)
        {
            normalMapAlphaEnvmapMask = false;
        }

        // Only the shader without a normal map or light warp can take the self illumination from the mask alpha, and it
        // cannot also use the base alpha
        var selfIllumFromMask = hasEnvmapMask
            && values.Float("$selfillum_envmapmask_alpha", 0f) != 0f
            && (isUnlit || (isVertexLit && !hasBump && !hasLightWarp && !phong));

        if (selfIllumFromMask)
        {
            selfIllum = false;
        }

        // The fresnel term is in the shaders with a normal map or light warp, which only have it without a detail texture,
        // and in the phong shader
        var selfIllumFresnel = selfIllum && wantsSelfIllumFresnel && (phong || ((hasBump || hasLightWarp) && !hasDetail));
        var useLightWarp = hasLightWarp && !(selfIllumFresnel && !phong);

        if (selfIllumFresnel && !phong)
        {
            normalMapAlphaEnvmapMask = false;
        }

        if (!hasEnvmapMask)
        {
            material.TextureParams.Remove("g_tEnvMapMask");
        }

        if (!hasBump)
        {
            material.TextureParams.Remove("g_tNormal2");
        }

        if (hasEnvmap && !envmap!.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase))
        {
            material.TextureParams["g_tEnvMap"] = VmtFile.NormalizePath(envmap, ".vtf");
        }

        // A surface that uses the alpha of its base texture for something else is not alpha tested
        var alphaTest = values.Boolean("$alphatest") && (phong || !(selfIllum || baseAlphaEnvmapMask));

        SetFlag(material, "F_ALPHA_TEST", alphaTest);
        SetFlag(material, "F_TRANSLUCENT", values.Boolean("$translucent") || (isWater && shader != "refract"));
        SetFlag(material, "F_ADDITIVE_BLEND", values.Boolean("$additive"));
        SetFlag(material, "F_RENDER_BACKFACES", values.Boolean("$nocull"));

        // Models are lit by their vertices and ignore the vertex colors of the mesh
        SetFlag(material, "F_VERTEX_COLOR", !isVertexLit && values.Boolean("$vertexcolor"));
        SetFlag(material, "F_VERTEX_ALPHA", !isVertexLit && values.Boolean("$vertexalpha"));
        SetFlag(material, "F_SELF_ILLUM", selfIllum);

        SetFlag(material, "F_BUMPMAP", hasBump);
        SetFlag(material, "F_BUMPMAP2", hasBump && material.TextureParams.ContainsKey("g_tNormal2"));
        SetFlag(material, "F_SSBUMP", hasBump && values.Boolean("$ssbump"));

        SetFlag(material, "F_ENVMAP", hasEnvmap);
        SetFlag(material, "F_ENVMAP_MASK", hasEnvmapMask);
        SetFlag(material, "F_BASE_ALPHA_ENVMAP_MASK", baseAlphaEnvmapMask);
        SetFlag(material, "F_NORMALMAP_ALPHA_ENVMAP_MASK", normalMapAlphaEnvmapMask);

        if (hasEnvmap)
        {
            var contrast = values.Float("$envmapcontrast", 0f);
            var saturation = values.Float("$envmapsaturation", 1f);
            var fresnelReflection = values.Float("$fresnelreflection", 1f);

            // The lightmapped shader only reads the contrast and the saturation when both change the reflection or when
            // the fresnel or a self illumination tint needs its slower variant. Otherwise it uses its fixed ones.
            if (isLightmapped)
            {
                var changesContrast = contrast != 0f && contrast != 1f && saturation != 1f;
                var hasSelfIllumTint = selfIllum && values.Vector("$selfillumtint", Vector3.One) != Vector3.One;

                if (!changesContrast && fresnelReflection == 1f && !hasSelfIllumTint)
                {
                    contrast = contrast == 1f ? 1f : 0f;
                    saturation = 1f;
                }
            }

            material.VectorParams["g_vEnvMapTint"] = new Vector4(values.Vector("$envmaptint", Vector3.One), 1f);
            material.FloatParams["g_flEnvMapContrast"] = contrast;
            material.FloatParams["g_flEnvMapSaturation"] = saturation;
            material.FloatParams["g_flEnvMapFresnel"] = values.Float("$envmapfresnel", 0f);
            material.FloatParams["g_flFresnelReflection"] = fresnelReflection;
        }

        SetFlag(material, "F_SELF_ILLUM_ENVMAPMASK_ALPHA", selfIllumFromMask);
        SetFlag(material, "F_SELF_ILLUM_FRESNEL", selfIllumFresnel);

        if (selfIllum || selfIllumFromMask)
        {
            material.VectorParams["g_vSelfIllumTint"] = new Vector4(values.Vector("$selfillumtint", Vector3.One), 1f);
        }

        if (selfIllumFresnel)
        {
            var minMaxExp = values.Vector("$selfillumfresnelminmaxexp", new Vector3(0f, 1f, 1f));
            var bias = minMaxExp.Y != 0f ? minMaxExp.X / minMaxExp.Y : 0f;

            // Scale, bias, exponent and brightness
            material.VectorParams["g_vSelfIllumFresnel"] = new Vector4(1f - bias, bias, minMaxExp.Z, minMaxExp.Y);
        }

        SetFlag(material, "F_LIGHTWARP", useLightWarp);
        SetFlag(material, "F_HALF_LAMBERT", phong || values.Boolean("$halflambert"));
        SetFlag(material, "F_PHONG", phong);

        if (phong)
        {
            var hasExponentTexture = material.TextureParams.ContainsKey("g_tPhongExponent");

            SetFlag(material, "F_PHONG_EXPONENT_TEXTURE", hasExponentTexture);
            SetFlag(material, "F_PHONG_WARP", material.TextureParams.ContainsKey("g_tPhongWarp"));
            SetFlag(material, "F_BASEMAP_ALPHA_PHONG_MASK", basemapAlphaPhongMask);
            SetFlag(material, "F_INVERT_PHONG_MASK", values.Boolean("$invertphongmask"));

            material.FloatParams["g_flPhongExponent"] = values.Float("$phongexponent", 0f);
            material.FloatParams["g_flPhongExponentFactor"] = values.Float("$phongexponentfactor", 0f);
            material.FloatParams["g_flPhongBoost"] = values.Float("$phongboost", 1f);

            var ranges = values.Vector("$phongfresnelranges", new Vector3(0f, 0.5f, 1f));
            material.VectorParams["g_vPhongFresnelRanges"] = new Vector4(ranges, 0f);

            // A zero tint tints by the albedo when the exponent texture has a channel for it, flagged with a negative red
            // channel, and by white otherwise
            var tint = values.Vector("$phongtint", Vector3.Zero);

            if (tint == Vector3.Zero)
            {
                tint = hasExponentTexture && values.Boolean("$phongalbedotint") ? new Vector3(-1f, 0f, 0f) : Vector3.One;
            }

            material.VectorParams["g_vPhongTint"] = new Vector4(tint, 0f);
        }

        var rimLight = phong && values.Boolean("$rimlight");
        SetFlag(material, "F_RIM_LIGHT", rimLight);

        if (rimLight)
        {
            SetFlag(material, "F_RIM_MASK", values.Boolean("$rimmask") && material.TextureParams.ContainsKey("g_tPhongExponent"));
            material.FloatParams["g_flRimLightExponent"] = MathF.Max(values.Float("$rimlightexponent", 4f), 1f);
            material.FloatParams["g_flRimLightBoost"] = values.Float("$rimlightboost", 1f);
        }

        if (shader == "eyerefract")
        {
            SetFlag(material, "F_RAYTRACE_SPHERE", values.Boolean("$raytracesphere", true));
            SetFlag(material, "F_SPHERE_TEXKILL", values.Boolean("$spheretexkillcombo", true));

            material.FloatParams["g_flDilation"] = values.Float("$dilation", 0.5f);
            material.FloatParams["g_flGlossiness"] = values.Float("$glossiness", 1f);
            material.FloatParams["g_flCorneaBumpStrength"] = values.Float("$corneabumpstrength", 1f);
            material.FloatParams["g_flParallaxStrength"] = values.Float("$parallaxstrength", 0.25f);
            material.FloatParams["g_flEyeballRadius"] = values.Float("$eyeballradius", 0.5f);
            material.VectorParams["g_vAmbientOcclColor"] = new Vector4(values.Vector("$ambientocclcolor", new Vector3(0.33f)), 0f);
        }

        if (isWater)
        {
            material.VectorParams["g_vWaterFogColor"] = new Vector4(values.Vector("$fogcolor", new Vector3(0.1f, 0.2f, 0.25f)), 1f);
            material.VectorParams["g_vRefractTint"] = new Vector4(values.Vector("$refracttint", Vector3.One), 1f);
            material.VectorParams["g_vReflectTint"] = new Vector4(values.Vector("$reflecttint", Vector3.One), 1f);
            material.FloatParams["g_flRefractAmount"] = values.Float("$refractamount", 0f);
            material.FloatParams["g_flWaterFogStart"] = values.Float("$fogstart", 0f);
            material.FloatParams["g_flWaterFogEnd"] = values.Float("$fogend", 0f);
            material.FloatParams["g_flCheapWaterStartDistance"] = values.Float("$cheapwaterstartdistance", 500f);
            material.FloatParams["g_flCheapWaterEndDistance"] = values.Float("$cheapwaterenddistance", 1000f);
            SetFlag(material, "F_REFRACT", values.Contains("$refracttexture") && !values.Boolean("$forcecheap"));
            SetFlag(material, "F_FADE_OUT_ON_SILHOUETTE", values.Boolean("$fadeoutonsilhouette"));
        }

        // Without a reference the alpha test passes above 0.7
        var alphaTestReference = values.Float("$alphatestreference", 0f);
        material.FloatParams["g_flAlphaTestReference"] = alphaTestReference > 0f ? alphaTestReference : 0.7f;
        material.FloatParams["g_flOpacityScale"] = values.Float("$alpha", 1f);

        SetFlag(material, "F_BASE_TEXTURE2", material.TextureParams.ContainsKey("g_tColor2"));
        SetFlag(material, "F_BLEND_MODULATE", material.TextureParams.ContainsKey("g_tBlendModulate"));
        SetFlag(material, "F_SELF_ILLUM_MASK", selfIllum && hasSelfIllumMask);
        SetFlag(material, "F_DETAIL", hasDetail);

        if (hasDetail)
        {
            material.FloatParams["g_flDetailScale"] = values.Float("$detailscale", 4f);
            material.FloatParams["g_flDetailBlendFactor"] = values.Float("$detailblendfactor", 1f);
            material.VectorParams["g_vDetailTint"] = new Vector4(values.Vector("$detailtint", Vector3.One), 1f);
            material.IntParams["g_nDetailBlendMode"] = values.Integer("$detailblendmode", 0);
        }

        // $color2 modulates the color of every shader, on top of $color
        var colorTint = values.Vector("$color", Vector3.One) * values.Vector("$color2", Vector3.One);
        material.VectorParams["g_vColorTint"] = new Vector4(colorTint, 1f);

        if (values.Boolean("$nofog"))
        {
            material.IntParams["g_bFogEnabled"] = 0;
        }

        if (values.Contains("$basetexturetransform"))
        {
            var transform = values.Transform("$basetexturetransform");
            material.VectorParams["g_vTexCoordTransformRow0"] = new Vector4(transform.M11, transform.M21, 0f, transform.M41);
            material.VectorParams["g_vTexCoordTransformRow1"] = new Vector4(transform.M12, transform.M22, 0f, transform.M42);
        }

        return material;
    }

    private static void SetFlag(ResourceTypes.Material material, string name, bool enabled)
        => material.IntParams[name] = enabled ? 1 : 0;
}
