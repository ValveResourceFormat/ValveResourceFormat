#version 460

#include "csgo_simple_liquid_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

// TODO: F_FOAM, F_IRIDESCENCE and the rain droplets of wet surfaces are not ported, and g_flTransmissiveStrength has no
// effect because the transmissive lighting term it scales is never accumulated.

uniform sampler2D g_tColorA; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormalA; // Sampler(UserConfig)
uniform sampler2D g_tLiquidMask; // Sampler(UserConfig)

#if (F_OPAQUE_REFRACT == 1)
    uniform sampler2D g_tSceneColor; // reserved
#endif

uniform float g_flMaskMinimum = 0.0;
uniform float g_flMaskMaximum = 1.0;
uniform float g_flFresnelGlassThickness = 0.4;
uniform float g_flCubeRefractBrightness = 1.0;
uniform float g_flCubeRefractTransparency = 0.5;
uniform float g_flRefractRoughnessMultiplier = 1.0;
uniform float g_flGlassRefraction = 1.03;
uniform float g_flBackgroundRefractionBlendStrength = 1.0;

#if (F_NO_LIQUID == 0)
    uniform float g_flLiquidWobbleSpeed = 1.0;
    uniform float g_flLiquidWobbleScale = 0.01;
    uniform float g_flLiquidWobbleWavelength = 1.0;
    uniform float g_flLiquidLevelHeight = 0.0;
    uniform float g_flLiquidLevelHeightDelta = 0.3;
    uniform vec3 g_vLiquidLevelUpwardsMinMidMax = vec3(-20.0, 0.0, 0.0);
    uniform vec3 g_vLiquidLevelDownwardsMinMidMax = vec3(-20.0, 0.0, 0.0);
    uniform vec3 g_vLiquidLevelSidewardsMinMidMax = vec3(-20.0, 0.0, 0.0);
    uniform float g_flLiquidColorHueShift = 0.0;
    uniform vec3 g_vLiquidColor = vec3(0.6, 0.4, 0.1); // SrgbRead(true)
    uniform float g_flLiquidBrightness = 1.0;
    uniform float g_flLiquidEmissiveStrength = 0.0;
    uniform float g_flLiquidInnerGlow = 0.1;
    uniform float g_flSurfaceTension = 0.5;
    uniform float g_flLiquidSharpness = 30.0;
    uniform float g_flBrightenEmptyArea = 3.0;
    uniform float g_flLiquidBackOffset = 6.0;
    uniform float g_flLiquidBackFade = 0.0;
    uniform float g_flLiquidBackCylinderOrSphere = 0.0;
    uniform float g_flLiquidRoughness = 0.3;
    uniform float g_flLiquidSpecularStrength = 1.0;
    uniform float g_flLiquidSurfaceSpecularStrength = 1.0;
    uniform float g_flWaterLineStrength = 0.5;
    uniform float g_flCubeRefractLiquidTransparency = 0.5;
    uniform float g_flLiquidRefraction = 0.75;
    uniform float g_flLiquidSurfaceRefraction = 0.8;
    uniform float g_flBubbleDepthFalloff = 1.5;
    uniform float g_flBubbleOpacity = 0.0;
    uniform float g_flBubbleScale = 0.6;
    uniform float g_flBubbleSpaceScale = 0.75;
    uniform float g_flBubbleSpeed = 1.0;
    uniform float g_flBubbleStrength = 5.0;
    uniform float g_flBubblesMaximum = 0.65;
    uniform float g_flBubblesMinimum = 0.15;
    uniform vec3 g_vBubbleColorInner = vec3(0.0); // SrgbRead(true)
    uniform vec3 g_vBubbleColorOuter = vec3(1.0); // SrgbRead(true)

    #if (F_USE_TEST_VALUES == 1)
        uniform float g_flTestAgitation = 0.3;
    #endif

    in vec3 vLiquidPositionOs;
    in vec3 vLiquidCenterWs;
    in vec3 vLiquidUpWs;
    in vec3 vLiquidGravityWs;

    // What GetMaterial() works out of the liquid for the lighting steps of main()
    struct LiquidState_t
    {
        float Coverage; // Where the liquid is seen through the glass
        float CoverageMasked;
        float WaterLine;
        float SurfaceBlend; // Where the surface of the liquid is seen rather than its body
        vec3 Emissive;
        vec3 Normal;
        vec2 SpecularRoughness;
    };

    LiquidState_t g_Liquid;
#endif

// The color texture, where the glass is from the liquid mask, and how much its edge thickens
vec3 g_vTextureColor = vec3(0.0);
float g_flGlassMask = 0.0;
float g_flGlassEdge = 0.0;
float g_flGlassFresnel = 0.0;

// What the light refracts through, the glass or the liquid behind it
vec3 g_vRefractionNormal = vec3(0.0);
float g_flRefractionIndex = 1.0;

#if (F_NO_LIQUID == 0)

// The time the liquid and its bubbles move by, wrapped so that it keeps its precision
float GetLiquidTime()
{
    return fract((g_flTime + vVertexColorOut.r) * 0.005) * 200.0;
}

// Fills the liquid state and returns the albedo of the bottle with the liquid seen through it.
// The vertex color of the model carries the fill of the bottle in red and the bubbles in green, the agitation of the
// liquid in blue.
vec3 GetLiquidAlbedo(MaterialProperties_t mat, vec4 normalTexture, vec3 glassAlbedo)
{
    float textureRoughness = normalTexture.z;
    vec3 viewToSurface = -mat.ViewDir;
    vec3 gravity = normalize(vLiquidGravityWs);
    vec3 up = -gravity;

    // TODO: Without the test values the game reads the agitation from pow(tint.b, 2) + 0.01 of the instance tint, and
    // shows a resting bottle without liquid. Neither this resting state nor decoding a white tint reproduces that.
#if (F_USE_TEST_VALUES == 1)
    float testAgitation = g_flTestAgitation;
#else
    float testAgitation = 0.0;
#endif

    float agitation = pow(testAgitation, 2.0) + 0.01;
    float wobbleTime = 3.0 * GetLiquidTime() * floor(g_flLiquidWobbleSpeed * 200.0);
    float bubbleTime = wobbleTime * 0.005;

    // The position on the screen plane, along the direction gravity pulls on the screen and across it
    vec3 centerToSurfaceVs = (g_matWorldToView * vec4(mat.PositionWS - vLiquidCenterWs, 0.0)).xyz;
    vec3 gravityVs = (g_matWorldToView * vec4(gravity, 0.0)).xyz;
    vec3 screenDown = normalize(vec3(gravityVs.xy, 0.0));
    vec2 screenPosition = vec2(dot(centerToSurfaceVs, cross(screenDown, vec3(0.0, 0.0, 1.0))), dot(centerToSurfaceVs, screenDown));

    // How far the bottle is tipped, picking the liquid levels of a standing, sideways or upside down bottle
    float tilt = dot(normalize(vLiquidUpWs), up);
    vec3 levels = mix(mix(g_vLiquidLevelDownwardsMinMidMax, g_vLiquidLevelSidewardsMinMidMax, saturate(tilt + 1.0)), g_vLiquidLevelUpwardsMinMidMax, saturate(tilt));
    float fill = ((1.0 - g_flLiquidLevelHeightDelta) + g_flLiquidLevelHeightDelta * vVertexColorOut.r) * g_flLiquidLevelHeight;
    float level = mix(mix(levels.x, levels.y, saturate(fill * 2.0)), levels.z, saturate((fill - 0.5) * 2.0)) + dot(vLiquidCenterWs, up);

    float surfaceFacing = length(mat.Normal * up);
    float surfaceTension = ((g_flGlassEdge * mix(0.25, 1.0, surfaceFacing)) + (pow(g_flGlassEdge, 5.0) * 0.8)) * g_flSurfaceTension;

    float tiltScale = mix(0.5, 1.0, abs(tilt));
    float wavelength = (g_flLiquidWobbleWavelength * tiltScale) * mix(0.2, 0.6, agitation);
    float phaseA = wobbleTime * 0.104;
    float phaseB = wobbleTime * -0.104;
    float steepness = 1.0 - surfaceFacing;

    float broad1 = agitation * exp(sin(dot(vec2(-1.24211, 1.142074) * wavelength, screenPosition) + wobbleTime * 0.034) - 1.0);
    float broad2 = exp(sin(dot(vec2(2.8211, 2.92074) * wavelength, screenPosition) + wobbleTime * 0.044) - 1.0);
    float broad3 = agitation * exp(sin(dot(vec2(-3.24211, -3.642074) * wavelength, screenPosition) + wobbleTime * 0.024) - 1.0);
    float broad4 = agitation * exp(sin(dot(vec2(3.74211, -3.642074) * wavelength, screenPosition) + wobbleTime * 0.029) - 1.0);
    float ripple1 = exp(sin(dot(vec2(-8.74211, 13.642074) * wavelength, screenPosition) + phaseA) * steepness * 0.14);
    float ripple2 = exp(sin(dot(vec2(13.74211, 14.642074) * wavelength, screenPosition) + phaseB) * steepness * 0.13);
    float ripple3 = exp(sin(dot(vec2(-23.74211, -13.642074) * wavelength, screenPosition) + phaseA) * steepness * 0.1);
    float ripple4 = exp(sin(dot(vec2(31.74211, -20.642074) * wavelength, screenPosition) + phaseB) * steepness * 0.08);
    float wave = (broad1 * 0.125 + broad2 * (1.0 / 3.0) + broad3 * (1.0 / 9.0) + broad4 * (1.0 / 9.0)) * ripple1 * ripple2 * ripple3 * ripple4;

    float wobble = wave * g_flLiquidWobbleScale * pow(agitation, 2.0) * tiltScale * 15.0 * (1.5 - g_flGlassEdge) - agitation * 0.25;

    float height = dot(mat.PositionWS, up);
    float viewAlongGravity = dot(viewToSurface, gravity);

    // The bubbles, four layers of them drifting up, as the size of their cells and as the normal they bend
    vec2 bubbleSpace = screenPosition * g_flBubbleSpaceScale + (normalTexture.xy - vec2(0.5)) * 0.25;
    float bubbleAmount = mix(g_flBubblesMinimum, g_flBubblesMaximum, pow(testAgitation, 9.0) + vVertexColorOut.g * 0.25);
    float bubbleSize = (1.5 * g_flBubbleScale * bubbleAmount) / (1.0 + abs(((level + surfaceTension + wobble) - height) * g_flBubbleDepthFalloff));

    bubbleSpace.y += 0.4 * wobble * bubbleSize;
    float sway1 = sin(bubbleSpace.y) * 0.25 + sin(bubbleSpace.y * 23.1984) * 0.02;
    float sway2 = sin(bubbleSpace.x * 1.294) * 0.25 + sin(bubbleSpace.x * 18.1984) * 0.04;
    float bubbleEdge = 4.0 * (1.0 - textureRoughness);
    float bubbleCellScale = 1.0 / max(bubbleSize, 0.001);

    vec2 cell1 = (fract(((bubbleSpace + vec2(0.2 - sway1, 0.356 + sway2)) * 0.6 + vec2(0.1, 1.0) * bubbleTime * 0.25 * g_flBubbleSpeed) * 1.74) * 2.0 - 1.0) * bubbleCellScale;
    float bubble1 = saturate(bubbleEdge * (1.0 - length(cell1)));
    vec2 dome1 = cell1 * pow(length(cell1), 2.0);

    vec2 cell2 = (fract(((bubbleSpace + vec2(-0.2 + sway1, -0.56 - sway2)) * 0.55 + vec2(-0.1, 1.0) * bubbleTime * 0.3 * g_flBubbleSpeed) * 2.74) * 2.0 - 1.0) * bubbleCellScale;
    float bubble2 = saturate(bubbleEdge * (1.0 - length(cell2)));
    vec2 dome2 = cell2 * pow(length(cell2), 2.0);

    vec2 cell3 = (fract(((bubbleSpace + vec2(0.35 - sway1 * 0.5, 0.6 - sway2 * 0.5)) * 0.5 + vec2(0.13, 1.0) * bubbleTime * 0.35 * g_flBubbleSpeed) * 4.74) * 2.0 - 1.0) * bubbleCellScale;
    float bubble3 = saturate(bubbleEdge * (1.0 - length(cell3)));
    vec2 dome3 = cell3 * pow(length(cell3), 2.0);

    vec2 cell4 = (fract(((bubbleSpace + vec2(-0.42 + sway1 * 0.5, -0.76 + sway2 * 0.5)) * 0.45 + vec2(-0.14, 1.0) * bubbleTime * 0.4 * g_flBubbleSpeed) * 5.34) * 2.0 - 1.0) * bubbleCellScale;
    float bubble4 = saturate(bubbleEdge * (1.0 - length(cell4)));
    vec2 dome4 = cell4 * pow(length(cell4), 2.0);

    vec3 bubbleColor = mix(g_vBubbleColorInner, g_vBubbleColorOuter, length(dome1)) * bubble1
        + mix(g_vBubbleColorInner, g_vBubbleColorOuter, length(dome2)) * bubble2
        + mix(g_vBubbleColorInner, g_vBubbleColorOuter, length(dome3)) * bubble3
        + mix(g_vBubbleColorInner, g_vBubbleColorOuter, length(dome4)) * bubble4;
    float bubbleDensity = (bubble1 + bubble2 + bubble3 + bubble4) * bubbleSize;

    float surfaceHeight = (level + wobble) + bubbleDensity * 0.025 - abs(viewAlongGravity) * 0.05;
    float sharpness = (g_flLiquidSharpness * g_flGlassMask) * mix(1.0, 0.2, pow(textureRoughness, 1.5));
    float tipFacing = min(0.3, saturate(1.0 - abs(gravity.z))) * (g_flGlassEdge - 0.2);
    float depthBelowSurface = height - (surfaceHeight + surfaceTension + 1.5 * tipFacing);

    float rim = 1.0 - g_flGlassEdge;
    float rimSquared = pow(rim, 2.0);
    float waterLine = ((1.0 - saturate((abs(depthBelowSurface - 0.05) * rimSquared) * sharpness)) * rimSquared) * g_flWaterLineStrength;
    float surfaceBlend = saturate((depthBelowSurface * sharpness) * 0.25);
    float aboveSurface = 1.0 - surfaceBlend;

    // The back wall of the bottle, which the liquid fills as far as it fills the front
    vec3 backPosition = vLiquidPositionOs;
    backPosition.z *= g_flLiquidBackCylinderOrSphere;
    float backOffset = (((1.5 * g_flLiquidBackOffset) * pow(saturate(viewAlongGravity), 2.0)) * rim) * length(backPosition);
    float backDepth = depthBelowSurface - backOffset;
    float backBlend = saturate((backDepth * sharpness) * 0.125);
    float backFade = 1.0 - saturate((saturate(depthBelowSurface / backOffset) * g_flLiquidBackFade) * 0.5);
    float backCoverage = (1.0 - backBlend) * backFade;

    float coverage = saturate((waterLine + aboveSurface) + backCoverage);
    float backSurface = saturate((backCoverage - aboveSurface) * g_flGlassMask);
    float coverageMasked = saturate(coverage * g_flGlassMask);

    vec2 bubbleBend = (((dome1 * bubble1) + (dome2 * bubble2) + (dome3 * bubble3) + (dome4 * bubble4)) * bubbleSize) * aboveSurface;

    // The color of the liquid with its hue shifted and desaturated towards gray where it is dull, tinted by the bubbles
    vec3 liquidColor = (g_vLiquidColor * g_flLiquidBrightness) * saturate(1.0 - waterLine);
    float hueShift = radians(g_flLiquidColorHueShift);
    float cosHue = cos(hueShift);
    float maxChannel = max(liquidColor.x, max(liquidColor.y, liquidColor.z));
    float saturation = 0.0;
    if (maxChannel != 0.0)
    {
        saturation = (maxChannel - min(liquidColor.x, min(liquidColor.y, liquidColor.z))) / maxChannel;
    }

    vec3 grayAxis = vec3(0.57735);
    vec3 shifted = (liquidColor * cosHue) + (cross(grayAxis, liquidColor) * sin(hueShift)) + ((grayAxis * dot(grayAxis, liquidColor)) * (1.0 - cosHue));
    vec3 bubbleTint = mix(vec3(1.0), bubbleColor * 4.0, saturate(g_flBubbleOpacity * (bubbleDensity * aboveSurface)));
    vec3 liquid = mix(vec3(GetLuma(liquidColor)), shifted, pow(saturation, 0.125)) * bubbleTint;

    vec3 albedo = glassAlbedo * mix(vec3(1.0), liquid, coverageMasked);
    albedo += (((liquid * g_flLiquidBrightness) * pow(rim, 3.0)) * coverageMasked) / (1.0 + max(surfaceHeight - height, 0.0)) * g_flLiquidInnerGlow;
    albedo *= 1.0 + (g_flBrightenEmptyArea * g_flGlassMask) * saturate(1.0 - coverage);

    // The normal of the liquid, which the bubbles bend and which turns to the flat surface of the liquid where that is seen
    vec3 bentNormal = normalize(mix(mat.Normal, mat.ViewDir, -0.3) + ((g_vCameraUpDirWs * bubbleBend.y + cross(g_vCameraUpDirWs, viewToSurface) * bubbleBend.x) * g_flBubbleStrength));
    vec3 flatNormal = normalize(vec3(wobble * 0.5, 0.0, 0.0) - gravity);
    float flatAmount = ((saturate(1.0 - ((saturate(depthBelowSurface) - saturate(backDepth)) * 0.5)) * saturate(coverage)) * backSurface) * (0.25 + (backFade * (2.0 / 3.0)));

    g_Liquid.Coverage = coverage;
    g_Liquid.CoverageMasked = coverageMasked;
    g_Liquid.WaterLine = waterLine;
    g_Liquid.SurfaceBlend = surfaceBlend;
    g_Liquid.Emissive = (liquid * coverageMasked) * g_flLiquidEmissiveStrength;
    g_Liquid.Normal = normalize(mix(bentNormal, flatNormal, flatAmount));

    float liquidAmount = saturate((surfaceBlend - backBlend) * 2.0);
    float waterLineSpecular = ((1.0 - saturate((abs(depthBelowSurface - 0.09) * rimSquared) * sharpness)) * rimSquared) * g_flWaterLineStrength;
    vec2 roughness = mix(vec2(textureRoughness), vec2(saturate(g_flLiquidRoughness)), liquidAmount);
    g_Liquid.SpecularRoughness = mix(roughness, vec2(0.75), waterLineSpecular * 2.0);

    float refractionIndex = mix(mix(g_flGlassRefraction, g_flLiquidRefraction, coverage), g_flLiquidSurfaceRefraction, backSurface);
    g_vRefractionNormal = mix(mat.Normal, mix(mat.Normal, g_Liquid.Normal, 0.75), coverage);
    g_flRefractionIndex = refractionIndex;

    return albedo;
}

#endif

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColorA, texCoord);
    vec4 normalTexture = texture(g_tNormalA, texCoord);
    g_vTextureColor = color.rgb;

    mat.NormalMap =DecodeHemiOctahedronNormal(normalTexture.rg);
    mat.RoughnessTex.xy = normalTexture.bb;
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    g_flGlassMask = saturate((texture(g_tLiquidMask, texCoord).r - g_flMaskMinimum) * (1.0 / (g_flMaskMaximum + 0.001)));
    g_flGlassEdge = saturate(1.0 - dot(mat.ViewDir, mat.Normal));
    g_flGlassFresnel = saturate(2.0 * (g_flGlassEdge - g_flFresnelGlassThickness));

    // The glass is darker towards its edge, where the light has to travel through more of it
    mat.Albedo = color.rgb * mix(1.0, 0.2, g_flGlassFresnel * g_flGlassMask);
    mat.Metalness = color.a;

    g_vRefractionNormal = mat.Normal;
    g_flRefractionIndex = g_flGlassRefraction;

#if (F_NO_LIQUID == 0)
    mat.Albedo = GetLiquidAlbedo(mat, normalTexture, mat.Albedo);
#endif

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

#if (S_MODE_DEPTH == 0)

// The light of the scene behind the surface, which the reflection captured in the environment map is scaled by.
// TODO: The game reads the light probe volumes here, also on lightmapped objects.
vec3 GetIrradianceBehind(MaterialProperties_t mat)
{
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 0) && (D_BAKED_LIGHTING_FROM_PROBE == 1)
    return SampleLightProbeIrradiance(mat.PositionWS, -mat.Normal);
#else
    return GetSkyAmbientLighting(-mat.Normal);
#endif
}

#if (F_OPAQUE_REFRACT == 1)
    // The scene behind the surface, shifted by how far the refraction bends the view, with the color channels offset against each other
    vec3 SampleBackgroundRefraction(vec3 viewToSurface, vec3 direction)
    {
        vec3 straight = refract(viewToSurface, g_vRefractionNormal, 1.0);
        vec2 offset = (g_matWorldToView * vec4(direction - straight, 0.0)).xy * (0.4 * 400.0);

        vec2 pixel = clamp(gl_FragCoord.xy + offset, vec2(0.0), g_vViewportSize);
        vec2 uv = pixel / vec2(textureSize(g_tSceneColor, 0));

        // Screen space is y up here
        vec3 center = texture(g_tSceneColor, uv).rgb;
        vec3 tap1 = texture(g_tSceneColor, uv + vec2(0.0007, -0.001)).rgb;
        vec3 tap2 = texture(g_tSceneColor, uv + vec2(0.0007, 0.001)).rgb;
        vec3 tap3 = texture(g_tSceneColor, uv + vec2(-0.0007, -0.001)).rgb;
        vec3 tap4 = texture(g_tSceneColor, uv + vec2(-0.0007, 0.001)).rgb;

        return (center * 2.0 + tap1 * vec3(2.0, 1.0, 0.0) + tap2 + tap3 * vec3(0.0, 1.0, 2.0) + tap4) / 6.0;
    }
#endif

// What the environment shows through the surface, refracted, in the color of the surface
vec3 GetCubeRefraction(MaterialProperties_t mat, float transparency)
{
    vec3 viewToSurface = -mat.ViewDir;
    vec3 refracted = refract(viewToSurface, g_vRefractionNormal, g_flRefractionIndex);
    vec3 direction = normalize(mix(reflect(viewToSurface, g_vRefractionNormal), refracted, length(refracted)));

    float roughness = mix(0.1, 1.0, saturate(dot(mat.RoughnessTex, vec2(0.5)) * g_flRefractRoughnessMultiplier));
    float lod = GetEnvMapLOD(sqrt(roughness));

    vec3 environment = vec3(0.3);
    if (g_vEnvMapSizeConstants.y != 0.0)
    {
        environment = BlendEnvMaps(direction, roughness, lod);
    }

    float environmentLuma = dot(vec4(direction, 1.0), g_vEnvMapNormalizationSH);
    environment *= GetEnvMapNormalization(roughness, environmentLuma, GetIrradianceBehind(mat));

#if (F_OPAQUE_REFRACT == 1)
    environment = mix(environment, SampleBackgroundRefraction(viewToSurface, direction), g_flBackgroundRefractionBlendStrength);
#endif

    vec3 tint = mix(mat.Albedo * mix(1.25, 0.75, g_flGlassEdge), vec3(1.0), saturate(transparency - 1.0));
    return tint * environment * g_flCubeRefractBrightness;
}

// The sun glinting off the far side of the glass
vec3 GetSunGlint(MaterialProperties_t mat, float transparency)
{
    float roughness = mat.RoughnessTex.x + 0.2;
    vec3 refracted = normalize(refract(-mat.ViewDir, g_vRefractionNormal, mix(g_flRefractionIndex, 1.0, 0.5)));
    float alignment = pow(saturate(dot(refracted, GetSunDirection())), 40.0 / roughness);
    float glint = (alignment + pow(alignment, 400.0 / roughness) * 4.0) * g_flGlassMask * 4.0;

    vec3 sunColor = GetSunColor();
    return mat.Albedo * (sunColor * sunColor * glint) * transparency * 2.0 * g_flCubeRefractBrightness * g_flGlassMask;
}

vec3 GetLiquidOutput(MaterialProperties_t mat, SurfaceOptions_t options, vec3 diffuseLighting, vec3 specularLighting)
{
    float transparency = g_flCubeRefractTransparency;

#if (F_NO_LIQUID == 0)
    transparency = mix(g_flCubeRefractTransparency, g_flCubeRefractLiquidTransparency, g_Liquid.Coverage);
#endif

    // The cube refraction both tints the diffuse light and adds light of its own
    vec3 cubeRefraction = GetCubeRefraction(mat, transparency);
    float refractionAmount = saturate(transparency * g_flGlassMask);
    vec3 refractionLight = cubeRefraction * refractionAmount;

    vec3 emissiveLight = refractionLight;
#if (F_NO_LIQUID == 0)
    emissiveLight += g_Liquid.Emissive;
#endif

    vec3 diffuseColor = mix(mat.DiffuseColor, cubeRefraction, refractionAmount);
    vec3 color = diffuseLighting * diffuseColor + emissiveLight + specularLighting;
    color += GetSunGlint(mat, transparency);

#if (F_NO_LIQUID == 0)
    // The liquid reflects the light again with its own normal and roughness, over the glass
    MaterialProperties_t liquidMat = mat;
    liquidMat.Normal = g_Liquid.Normal;
    liquidMat.AmbientNormal = g_Liquid.Normal;
    liquidMat.RoughnessTex = g_Liquid.SpecularRoughness;
    AdjustRoughnessByGeometricNormal(liquidMat);

    LightingTerms_t liquidLighting;
    vec3 liquidDiffuseLighting;
    vec3 liquidSpecularLighting;
    LightSurface(liquidMat, liquidLighting, options, liquidDiffuseLighting, liquidSpecularLighting);

    float liquidSpecularStrength = mix(g_flLiquidSpecularStrength, g_flLiquidSurfaceSpecularStrength * saturate(dot(-mat.ViewDir, normalize(vLiquidGravityWs)) * 4.0), g_Liquid.SurfaceBlend);

    vec3 liquidReflection = emissiveLight + liquidSpecularLighting;
    vec3 liquidTint = mix(g_vTextureColor, color, 0.5);
    color += (((liquidReflection * liquidTint) * liquidSpecularStrength) * g_Liquid.CoverageMasked) * (1.0 - g_Liquid.WaterLine) * (1.0 - g_flGlassFresnel);
#endif

    return color;
}

#endif

void main()
{
#if (S_MODE_DEPTH != 0)
    RenderDepthPass();
#else
    MaterialProperties_t mat = GetShadedMaterial();

    LightingTerms_t lighting;
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;

    vec3 diffuseLighting;
    vec3 specularLighting;
    LightSurface(mat, lighting, options, diffuseLighting, specularLighting);

    outputColor = vec4(GetLiquidOutput(mat, options, diffuseLighting, specularLighting), 1.0);

    FinishSurface(mat, lighting, options, g_tColorA);
#endif
}
