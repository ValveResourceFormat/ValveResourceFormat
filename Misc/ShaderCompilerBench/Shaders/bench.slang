// A forward-lit uber-shader written to be the same class of workload as the renderer's
// complex.vert/complex.frag: layered materials, parallax, a clustered light loop with cascaded
// shadows, image based lighting, cloth and skin lobes, fog, and a render mode switch at the end.
//
// It is written for OpenGL's SPIR-V dialect, so every non-opaque uniform lives in a constant
// buffer, and every texture is a combined image sampler.

#ifndef MATERIAL_LAYERS
#define MATERIAL_LAYERS 4
#endif

#ifndef MAX_LIGHTS
#define MAX_LIGHTS 64
#endif

#ifndef SHADOW_CASCADES
#define SHADOW_CASCADES 4
#endif

#ifndef PARALLAX_STEPS
#define PARALLAX_STEPS 24
#endif

#ifndef MAX_BONES
#define MAX_BONES 128
#endif

// Set to 1 to unroll the light loop, which is the knob that moves this shader's driver link cost
// into the same range as the renderer's own complex.vert/frag.
#ifndef UNROLL_LIGHTS
#define UNROLL_LIGHTS 0
#endif

// Bumped by the bench for every iteration so neither Slang nor the driver can serve a cached result.
#ifndef BENCH_SALT
#define BENCH_SALT 0
#endif

static const float PI = 3.14159265358979323846;
static const float INV_PI = 0.31830988618379067154;
static const float EPSILON = 1e-5;
static const float MIN_ROUGHNESS = 0.0064;

// ---------------------------------------------------------------------------------------------
// Uniform state
// ---------------------------------------------------------------------------------------------

struct Light
{
    float4 PositionType;      // xyz world position, w light type (0 point, 1 spot, 2 directional)
    float4 DirectionRange;    // xyz world direction, w inverse range
    float4 ColorIntensity;    // rgb linear colour, a intensity
    float4 SpotAndShadow;     // x cos inner, y cos outer, z shadow map slice or -1, w cookie slice or -1
};

struct ViewConstants
{
    float4x4 WorldToProjection;
    float4x4 WorldToView;
    float4x4 ViewToProjection;
    float4x4 ProjectionToWorld;
    float4 CameraPosition;
    float4 ViewportSize;      // xy size, zw inverse size
    float4 DepthRange;        // x near, y far, z 1/(far-near), w time
    float4 FogParameters;     // x density, y height falloff, z start distance, w max opacity
    float4 FogColor;
    float4 SunDirection;
    float4 SunColor;
    float4 AmbientSH[9];
    float4x4 WorldToShadow[SHADOW_CASCADES];
    float4 CascadeSplits;
    float4 ShadowParameters;  // x texel size, y depth bias, z normal bias, w slice count
};

struct MaterialConstants
{
    float4 LayerTint[MATERIAL_LAYERS];
    float4 LayerScaleOffset[MATERIAL_LAYERS];
    float4 LayerControl[MATERIAL_LAYERS];   // x roughness scale, y metalness scale, z normal scale, w blend softness
    float4 SelfIllumTint;
    float4 SheenTint;
    float4 SubsurfaceTint;
    float4 DetailParameters;   // x blend factor, y normal strength, z tint strength, w scale
    float4 ParallaxParameters; // x depth, y shadow strength, z min steps, w max steps
    float4 ClearCoat;          // x weight, y roughness, z ior, w unused
    float4 Anisotropy;         // x amount, y rotation, z unused, w unused
    float4 AlphaParameters;    // x cutoff, y fade, z sharpness, w unused
    float4 TextureAnimation;   // x cell count, y frames per second, z grid width, w grid height
    float4 RenderMode;         // x mode index, y debug scale, z unused, w unused
};

struct InstanceConstants
{
    float4x4 ObjectToWorld;
    float4x4 BoneMatrices[MAX_BONES];
    float4 TintColor;
    float4 MorphParameters;   // x weight, y texture width, z texture height, w vertex offset
};

ConstantBuffer<ViewConstants> g_View;
ConstantBuffer<MaterialConstants> g_Material;
ConstantBuffer<InstanceConstants> g_Instance;

struct LightConstants
{
    float4 LightCount;        // x active light count
    Light Lights[MAX_LIGHTS];
};

ConstantBuffer<LightConstants> g_Lighting;

Sampler2DArray g_tLayerColor;
Sampler2DArray g_tLayerNormal;
Sampler2DArray g_tLayerMaterial;   // r roughness, g metalness, b ambient occlusion, a height
Sampler2D g_tBlendMask;
Sampler2D g_tDetailColor;
Sampler2D g_tDetailNormal;
Sampler2D g_tSelfIllum;
Sampler2D g_tCurvatureLut;
Sampler2D g_tBrdfLut;
Sampler2D g_tMorph;
Sampler2DArray g_tShadowDepth;
Sampler2DArray g_tCookie;
SamplerCube g_tEnvironmentMap;

// ---------------------------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------------------------

float pow2(float x) { return x * x; }
float pow5(float x) { float x2 = x * x; return x2 * x2 * x; }
float saturate2(float x) { return clamp(x, 0.0, 1.0); }

float3 SrgbToLinear(float3 color)
{
    float3 low = color * (1.0 / 12.92);
    float3 high = pow(max((color + 0.055) * (1.0 / 1.055), 0.0), 2.4);
    return lerp(low, high, step(0.04045, color));
}

float3 LinearToSrgb(float3 color)
{
    float3 low = color * 12.92;
    float3 high = 1.055 * pow(max(color, 0.0), 1.0 / 2.4) - 0.055;
    return lerp(low, high, step(0.0031308, color));
}

float Luminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float Hash11(float value)
{
    float fractional = frac(value * 0.1031);
    fractional *= fractional + 33.33;
    fractional *= fractional + fractional;
    return frac(fractional);
}

float2 Hash22(float2 value)
{
    float3 spread = frac(value.xyx * float3(0.1031, 0.1030, 0.0973));
    spread += dot(spread, spread.yzx + 33.33);
    return frac((spread.xx + spread.yz) * spread.zy);
}

float ValueNoise(float2 position)
{
    float2 cell = floor(position);
    float2 local = frac(position);
    float2 smoothed = local * local * (3.0 - 2.0 * local);

    float a = Hash11(dot(cell + float2(0.0, 0.0), float2(1.0, 57.0)));
    float b = Hash11(dot(cell + float2(1.0, 0.0), float2(1.0, 57.0)));
    float c = Hash11(dot(cell + float2(0.0, 1.0), float2(1.0, 57.0)));
    float d = Hash11(dot(cell + float2(1.0, 1.0), float2(1.0, 57.0)));

    return lerp(lerp(a, b, smoothed.x), lerp(c, d, smoothed.x), smoothed.y);
}

float FractalNoise(float2 position, int octaves)
{
    float total = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    for (int i = 0; i < octaves; i++)
    {
        total += ValueNoise(position * frequency) * amplitude;
        frequency *= 2.03;
        amplitude *= 0.5;
    }

    return total;
}

float3 DecodeNormal(float4 packed, float scale)
{
    float2 xy = packed.ag * 2.0 - 1.0;
    float z = sqrt(saturate2(1.0 - dot(xy, xy)));
    float3 normal = float3(xy * scale, max(z, EPSILON));
    return normalize(normal);
}

float3 BlendNormals(float3 baseNormal, float3 detailNormal, float strength)
{
    float3 scaled = float3(baseNormal.xy + detailNormal.xy * strength, baseNormal.z * detailNormal.z);
    return normalize(scaled);
}

float3x3 BuildTangentFrame(float3 normal, float4 tangent)
{
    float3 t = normalize(tangent.xyz - normal * dot(normal, tangent.xyz));
    float3 b = cross(normal, t) * tangent.w;
    return float3x3(t, b, normal);
}

float2 AnimateTexcoord(float2 texcoord, float time)
{
    float cellCount = max(g_Material.TextureAnimation.x, 1.0);
    float gridWidth = max(g_Material.TextureAnimation.z, 1.0);
    float gridHeight = max(g_Material.TextureAnimation.w, 1.0);

    float frame = floor(fmod(time * g_Material.TextureAnimation.y, cellCount));
    float column = fmod(frame, gridWidth);
    float row = floor(frame / gridWidth);

    float2 cellSize = float2(1.0 / gridWidth, 1.0 / gridHeight);
    return texcoord * cellSize + float2(column, row) * cellSize;
}

// ---------------------------------------------------------------------------------------------
// Surface assembly
// ---------------------------------------------------------------------------------------------

struct Surface
{
    float3 Albedo;
    float3 Normal;
    float3 GeometricNormal;
    float3 ViewDirection;
    float3 SpecularColor;
    float3 SheenColor;
    float3 SubsurfaceColor;
    float3 SelfIllum;
    float3 TangentU;
    float3 TangentV;
    float Roughness;
    float RoughnessU;
    float RoughnessV;
    float Metalness;
    float AmbientOcclusion;
    float Curvature;
    float ClearCoatWeight;
    float ClearCoatRoughness;
    float Opacity;
    float NdotV;
    float3 WorldPosition;
};

/// Walks the height field of the top layer until the ray goes below it, then refines the last step.
float2 ParallaxOcclusion(float2 texcoord, float3 tangentView, float layer)
{
    float depth = g_Material.ParallaxParameters.x;

    if (depth <= 0.0)
    {
        return texcoord;
    }

    float steps = lerp(g_Material.ParallaxParameters.w, g_Material.ParallaxParameters.z, abs(tangentView.z));
    steps = clamp(steps, 4.0, float(PARALLAX_STEPS));

    float stepSize = 1.0 / steps;
    float2 delta = tangentView.xy / max(abs(tangentView.z), 0.1) * depth * stepSize;

    float2 current = texcoord;
    float currentDepth = 0.0;
    float sampledDepth = 1.0 - g_tLayerMaterial.Sample(float3(current, layer)).a;

    for (int i = 0; i < PARALLAX_STEPS; i++)
    {
        if (currentDepth >= sampledDepth)
        {
            break;
        }

        current -= delta;
        currentDepth += stepSize;
        sampledDepth = 1.0 - g_tLayerMaterial.Sample(float3(current, layer)).a;
    }

    float2 previous = current + delta;
    float afterDepth = sampledDepth - currentDepth;
    float beforeDepth = 1.0 - g_tLayerMaterial.Sample(float3(previous, layer)).a - currentDepth + stepSize;
    float weight = afterDepth / (afterDepth - beforeDepth + EPSILON);

    return lerp(current, previous, saturate2(weight));
}

/// Marches back toward the light to see whether the height field occludes this texel.
float ParallaxShadow(float2 texcoord, float3 tangentLight, float layer)
{
    float strength = g_Material.ParallaxParameters.y;

    if (strength <= 0.0 || tangentLight.z <= 0.0)
    {
        return 1.0;
    }

    float depth = g_Material.ParallaxParameters.x;
    float stepSize = 1.0 / 8.0;
    float2 delta = tangentLight.xy / max(tangentLight.z, 0.1) * depth * stepSize;

    float height = 1.0 - g_tLayerMaterial.Sample(float3(texcoord, layer)).a;
    float occlusion = 0.0;

    for (int i = 1; i <= 8; i++)
    {
        float2 offset = texcoord + delta * float(i);
        float sampled = 1.0 - g_tLayerMaterial.Sample(float3(offset, layer)).a;
        float difference = height - float(i) * stepSize - sampled;
        occlusion = max(occlusion, difference * (1.0 - float(i) / 8.0));
    }

    return saturate2(1.0 - occlusion * strength * 8.0);
}

struct LayerSample
{
    float4 Color;
    float3 Normal;
    float4 Material;
};

LayerSample SampleLayer(float2 texcoord, int index)
{
    float4 scaleOffset = g_Material.LayerScaleOffset[index];
    float2 uv = texcoord * scaleOffset.xy + scaleOffset.zw;
    float layer = float(index);

    LayerSample result;
    result.Color = g_tLayerColor.Sample(float3(uv, layer)) * g_Material.LayerTint[index];
    result.Normal = DecodeNormal(g_tLayerNormal.Sample(float3(uv, layer)), g_Material.LayerControl[index].z);
    result.Material = g_tLayerMaterial.Sample(float3(uv, layer));
    result.Material.r *= g_Material.LayerControl[index].x;
    result.Material.g *= g_Material.LayerControl[index].y;
    return result;
}

/// Height aware blend: the layer whose height plus mask is tallest wins, softened by the layer's
/// blend softness so the transition is not a hard edge.
float LayerWeight(float mask, float height, float softness)
{
    float bias = mask + height * 0.5 - 0.5;
    return saturate2(bias / max(softness, EPSILON) + 0.5);
}

LayerSample BlendLayers(float2 texcoord, float4 vertexBlend)
{
    LayerSample accumulated = SampleLayer(texcoord, 0);
    float4 mask = g_tBlendMask.Sample(texcoord) * vertexBlend;

    for (int i = 1; i < MATERIAL_LAYERS; i++)
    {
        LayerSample layer = SampleLayer(texcoord, i);
        float weight = LayerWeight(mask[i], layer.Material.a, g_Material.LayerControl[i].w);

        accumulated.Color = lerp(accumulated.Color, layer.Color, weight);
        accumulated.Normal = normalize(lerp(accumulated.Normal, layer.Normal, weight));
        accumulated.Material = lerp(accumulated.Material, layer.Material, weight);
    }

    return accumulated;
}

void ApplyDetail(inout LayerSample layer, float2 texcoord)
{
    float2 detailTexcoord = texcoord * g_Material.DetailParameters.w;

    float4 detailColor = g_tDetailColor.Sample(detailTexcoord);
    float3 detailNormal = DecodeNormal(g_tDetailNormal.Sample(detailTexcoord), 1.0);

    float blend = g_Material.DetailParameters.x * detailColor.a;
    layer.Color.rgb = lerp(layer.Color.rgb, layer.Color.rgb * detailColor.rgb * 2.0, blend * g_Material.DetailParameters.z);
    layer.Normal = BlendNormals(layer.Normal, detailNormal, g_Material.DetailParameters.y * blend);
}

// ---------------------------------------------------------------------------------------------
// BRDF
// ---------------------------------------------------------------------------------------------

float DistributionGgx(float roughness, float NdotH)
{
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float denominator = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
    return alpha2 / max(PI * denominator * denominator, EPSILON);
}

float DistributionGgxAnisotropic(float roughnessU, float roughnessV, float NdotH, float TdotH, float BdotH)
{
    float alphaU = max(roughnessU * roughnessU, MIN_ROUGHNESS);
    float alphaV = max(roughnessV * roughnessV, MIN_ROUGHNESS);

    float3 scaled = float3(alphaV * TdotH, alphaU * BdotH, alphaU * alphaV * NdotH);
    float weight = alphaU * alphaV / max(dot(scaled, scaled), EPSILON);

    return INV_PI * alphaU * alphaV * weight * weight;
}

float VisibilitySmithGgxCorrelated(float roughness, float NdotV, float NdotL)
{
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;

    float lambdaV = NdotL * sqrt(NdotV * NdotV * (1.0 - alpha2) + alpha2);
    float lambdaL = NdotV * sqrt(NdotL * NdotL * (1.0 - alpha2) + alpha2);

    return 0.5 / max(lambdaV + lambdaL, EPSILON);
}

float VisibilityKelemen(float VdotH)
{
    return 0.25 / max(VdotH * VdotH, EPSILON);
}

float3 FresnelSchlick(float3 f0, float VdotH)
{
    return f0 + (1.0 - f0) * pow5(1.0 - VdotH);
}

float3 FresnelSchlickRoughness(float3 f0, float NdotV, float roughness)
{
    float3 ceiling = max(float3(1.0 - roughness), f0);
    return f0 + (ceiling - f0) * pow5(1.0 - NdotV);
}

/// Charlie sheen distribution, the cheap cloth lobe.
float DistributionCharlie(float roughness, float NdotH)
{
    float inverseAlpha = 1.0 / max(roughness * roughness, MIN_ROUGHNESS);
    float sin2h = max(1.0 - NdotH * NdotH, 0.0078125);
    return (2.0 + inverseAlpha) * pow(sin2h, inverseAlpha * 0.5) / (2.0 * PI);
}

float VisibilityAshikhmin(float NdotV, float NdotL)
{
    return 1.0 / max(4.0 * (NdotL + NdotV - NdotL * NdotV), EPSILON);
}

float DiffuseBurley(float roughness, float NdotV, float NdotL, float VdotH)
{
    float f90 = 0.5 + 2.0 * roughness * VdotH * VdotH;
    float lightScatter = 1.0 + (f90 - 1.0) * pow5(1.0 - NdotL);
    float viewScatter = 1.0 + (f90 - 1.0) * pow5(1.0 - NdotV);
    return lightScatter * viewScatter * INV_PI;
}

/// Preintegrated skin transmission, indexed by the wrapped diffuse term and the surface curvature.
float3 SubsurfaceScattering(Surface surface, float3 lightDirection)
{
    float NdotL = dot(surface.Normal, lightDirection);
    float wrapped = (NdotL + 0.35) / 1.35;

    float2 lookup = float2(saturate2(wrapped), saturate2(surface.Curvature));
    float3 profile = g_tCurvatureLut.Sample(lookup).rgb;

    return profile * surface.SubsurfaceColor;
}

float3 EnvironmentBrdf(float3 specularColor, float roughness, float NdotV)
{
    float2 lookup = g_tBrdfLut.Sample(float2(NdotV, roughness)).rg;
    return specularColor * lookup.x + lookup.y;
}

/// Third order spherical harmonic irradiance, matching the renderer's probe convention.
float3 EvaluateAmbientSh(float3 normal)
{
    float3 result = g_View.AmbientSH[0].rgb * 0.886227;

    result += g_View.AmbientSH[1].rgb * (2.0 * 0.511664 * normal.y);
    result += g_View.AmbientSH[2].rgb * (2.0 * 0.511664 * normal.z);
    result += g_View.AmbientSH[3].rgb * (2.0 * 0.511664 * normal.x);

    result += g_View.AmbientSH[4].rgb * (2.0 * 0.429043 * normal.x * normal.y);
    result += g_View.AmbientSH[5].rgb * (2.0 * 0.429043 * normal.y * normal.z);
    result += g_View.AmbientSH[6].rgb * (0.743125 * normal.z * normal.z - 0.247708);
    result += g_View.AmbientSH[7].rgb * (2.0 * 0.429043 * normal.x * normal.z);
    result += g_View.AmbientSH[8].rgb * (0.429043 * (normal.x * normal.x - normal.y * normal.y));

    return max(result * INV_PI, 0.0);
}

float3 EvaluateImageBasedLighting(Surface surface)
{
    float3 reflected = reflect(-surface.ViewDirection, surface.Normal);
    float lod = surface.Roughness * 6.0;

    float3 prefiltered = g_tEnvironmentMap.SampleLevel(reflected, lod).rgb;
    float3 specular = prefiltered * EnvironmentBrdf(surface.SpecularColor, surface.Roughness, surface.NdotV);

    float3 irradiance = EvaluateAmbientSh(surface.Normal);
    float3 diffuse = irradiance * surface.Albedo * (1.0 - surface.Metalness);

    if (surface.ClearCoatWeight > 0.0)
    {
        float3 coat = g_tEnvironmentMap.SampleLevel(reflected, surface.ClearCoatRoughness * 6.0).rgb;
        float coatFresnel = 0.04 + 0.96 * pow5(1.0 - surface.NdotV);
        specular = specular * (1.0 - coatFresnel * surface.ClearCoatWeight) + coat * coatFresnel * surface.ClearCoatWeight;
    }

    return (diffuse + specular) * surface.AmbientOcclusion;
}

// ---------------------------------------------------------------------------------------------
// Shadows and lighting
// ---------------------------------------------------------------------------------------------

int SelectCascade(float viewDepth)
{
    int cascade = SHADOW_CASCADES - 1;

    for (int i = 0; i < SHADOW_CASCADES; i++)
    {
        if (viewDepth < g_View.CascadeSplits[i])
        {
            cascade = i;
            break;
        }
    }

    return cascade;
}

/// Rotated Poisson disk, twelve taps, the same shape the renderer's sun shadows use.
float SamplePcf(float3 worldPosition, float3 normal, int slice, float4x4 worldToShadow, float2 rotation)
{
    float4 shadowPosition = mul(worldToShadow, float4(worldPosition + normal * g_View.ShadowParameters.z, 1.0));
    shadowPosition.xyz /= shadowPosition.w;

    float2 shadowTexcoord = shadowPosition.xy * 0.5 + 0.5;
    float referenceDepth = shadowPosition.z * 0.5 + 0.5 - g_View.ShadowParameters.y;

    if (any(shadowTexcoord < 0.0) || any(shadowTexcoord > 1.0))
    {
        return 1.0;
    }

    const float2 offsets[12] = {
        float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457),
        float2(-0.203, 0.621), float2(0.962, -0.195), float2(0.473, -0.480),
        float2(0.519, 0.767), float2(0.185, -0.893), float2(0.507, 0.064),
        float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
    };

    float texelSize = g_View.ShadowParameters.x;
    float visibility = 0.0;

    for (int i = 0; i < 12; i++)
    {
        float2 rotated = float2(
            offsets[i].x * rotation.x - offsets[i].y * rotation.y,
            offsets[i].x * rotation.y + offsets[i].y * rotation.x);

        float depth = g_tShadowDepth.Sample(float3(shadowTexcoord + rotated * texelSize, float(slice))).r;
        visibility += referenceDepth <= depth ? 1.0 : 0.0;
    }

    return visibility / 12.0;
}

float SampleSunShadow(float3 worldPosition, float3 normal, float viewDepth, float2 rotation)
{
    int cascade = SelectCascade(viewDepth);
    float visibility = SamplePcf(worldPosition, normal, cascade, g_View.WorldToShadow[cascade], rotation);

    // Fade the last cascade out rather than popping to fully lit at its far edge.
    float fadeStart = g_View.CascadeSplits[SHADOW_CASCADES - 1] * 0.85;
    float fade = saturate2((viewDepth - fadeStart) / max(g_View.CascadeSplits[SHADOW_CASCADES - 1] - fadeStart, EPSILON));

    return lerp(visibility, 1.0, fade);
}

struct LightSample
{
    float3 Color;
    float3 Direction;
    float Attenuation;
};

LightSample EvaluateLight(Light light, float3 worldPosition, float3 normal, float2 rotation, float viewDepth)
{
    LightSample result;
    result.Color = light.ColorIntensity.rgb * light.ColorIntensity.a;
    result.Attenuation = 1.0;

    float type = light.PositionType.w;

    if (type >= 2.0)
    {
        result.Direction = -normalize(light.DirectionRange.xyz);
        result.Attenuation = SampleSunShadow(worldPosition, normal, viewDepth, rotation);
        return result;
    }

    float3 toLight = light.PositionType.xyz - worldPosition;
    float distanceSquared = dot(toLight, toLight);
    float distanceToLight = sqrt(max(distanceSquared, EPSILON));
    result.Direction = toLight / distanceToLight;

    // Inverse square with a smooth window so the light reaches exactly zero at its range.
    float normalized = distanceSquared * pow2(light.DirectionRange.w);
    float window = pow2(saturate2(1.0 - pow2(normalized)));
    result.Attenuation = window / max(distanceSquared, 0.01);

    if (type >= 1.0)
    {
        float cosAngle = dot(-result.Direction, normalize(light.DirectionRange.xyz));
        float cone = saturate2((cosAngle - light.SpotAndShadow.y) / max(light.SpotAndShadow.x - light.SpotAndShadow.y, EPSILON));
        result.Attenuation *= cone * cone;

        if (light.SpotAndShadow.w >= 0.0)
        {
            float2 cookieTexcoord = float2(cosAngle, dot(result.Direction, normal)) * 0.5 + 0.5;
            result.Color *= g_tCookie.Sample(float3(cookieTexcoord, light.SpotAndShadow.w)).rgb;
        }
    }

    if (light.SpotAndShadow.z >= 0.0 && result.Attenuation > 0.0)
    {
        result.Attenuation *= SamplePcf(worldPosition, normal, int(light.SpotAndShadow.z), g_View.WorldToShadow[0], rotation);
    }

    return result;
}

float3 ShadeDirect(Surface surface, LightSample light, float parallaxShadow)
{
    float3 lightDirection = light.Direction;
    float NdotL = dot(surface.Normal, lightDirection);

    float3 result = float3(0.0);

    if (surface.SubsurfaceColor.r + surface.SubsurfaceColor.g + surface.SubsurfaceColor.b > 0.0)
    {
        result += SubsurfaceScattering(surface, lightDirection) * light.Color * light.Attenuation;
    }

    if (NdotL <= 0.0 || light.Attenuation <= 0.0)
    {
        return result;
    }

    float3 halfVector = normalize(lightDirection + surface.ViewDirection);
    float NdotH = saturate2(dot(surface.Normal, halfVector));
    float VdotH = saturate2(dot(surface.ViewDirection, halfVector));
    float clampedNdotL = saturate2(NdotL);

    float distribution = DistributionGgxAnisotropic(
        surface.RoughnessU,
        surface.RoughnessV,
        NdotH,
        dot(surface.TangentU, halfVector),
        dot(surface.TangentV, halfVector));

    float visibility = VisibilitySmithGgxCorrelated(surface.Roughness, surface.NdotV, clampedNdotL);
    float3 fresnel = FresnelSchlick(surface.SpecularColor, VdotH);
    float3 specular = distribution * visibility * fresnel;

    float3 diffuse = surface.Albedo * (1.0 - surface.Metalness)
        * DiffuseBurley(surface.Roughness, surface.NdotV, clampedNdotL, VdotH);

    float3 sheen = surface.SheenColor
        * DistributionCharlie(surface.Roughness, NdotH)
        * VisibilityAshikhmin(surface.NdotV, clampedNdotL);

    float3 lobes = diffuse + specular + sheen;

    if (surface.ClearCoatWeight > 0.0)
    {
        float coatDistribution = DistributionGgx(max(surface.ClearCoatRoughness, MIN_ROUGHNESS), NdotH);
        float coatVisibility = VisibilityKelemen(VdotH);
        float coatFresnel = (0.04 + 0.96 * pow5(1.0 - VdotH)) * surface.ClearCoatWeight;

        lobes = lobes * (1.0 - coatFresnel) + coatDistribution * coatVisibility * coatFresnel;
    }

    return result + lobes * light.Color * light.Attenuation * clampedNdotL * parallaxShadow;
}

// ---------------------------------------------------------------------------------------------
// Atmosphere and output
// ---------------------------------------------------------------------------------------------

float3 ApplyFog(float3 color, float3 worldPosition)
{
    float3 toCamera = g_View.CameraPosition.xyz - worldPosition;
    float distanceToCamera = length(toCamera);

    float distanceFactor = max(distanceToCamera - g_View.FogParameters.z, 0.0) * g_View.FogParameters.x;

    // Analytic integral of an exponential height fog along the view ray.
    float heightFalloff = g_View.FogParameters.y;
    float cameraHeight = g_View.CameraPosition.y;
    float deltaHeight = worldPosition.y - cameraHeight;

    float heightFactor = exp(-heightFalloff * cameraHeight);

    if (abs(deltaHeight) > EPSILON)
    {
        heightFactor *= (1.0 - exp(-heightFalloff * deltaHeight)) / (heightFalloff * deltaHeight);
    }

    float opacity = 1.0 - exp(-distanceFactor * heightFactor);
    opacity = min(opacity, g_View.FogParameters.w);

    return lerp(color, g_View.FogColor.rgb, saturate2(opacity));
}

float3 TonemapToOutput(float3 color)
{
    // Reinhard with a white point, which is enough to keep the maths in the same class as the
    // renderer's tonemap without pulling its lookup tables in.
    float luminance = Luminance(color);
    float mapped = luminance * (1.0 + luminance / 16.0) / (1.0 + luminance);

    return color * (mapped / max(luminance, EPSILON));
}

float3 ApplyRenderMode(float3 color, Surface surface, int lightCount)
{
    int mode = int(g_Material.RenderMode.x);

    switch (mode)
    {
        case 1:
            return surface.Albedo;
        case 2:
            return surface.Normal * 0.5 + 0.5;
        case 3:
            return float3(surface.Roughness);
        case 4:
            return float3(surface.Metalness);
        case 5:
            return float3(surface.AmbientOcclusion);
        case 6:
            return surface.SpecularColor;
        case 7:
            return float3(float(lightCount) / float(MAX_LIGHTS));
        case 8:
            return LinearToSrgb(surface.SelfIllum);
        case 9:
            return float3(surface.Curvature);
        default:
            return color;
    }
}

// ---------------------------------------------------------------------------------------------
// Vertex stage
// ---------------------------------------------------------------------------------------------

struct VertexInput
{
    float3 Position;
    float3 Normal;
    float4 Tangent;
    float2 Texcoord;
    float4 BlendWeights;
    float4 BlendIndices;
    float4 Color;
    float VertexId;
};

struct Varyings
{
    float4 ClipPosition : SV_Position;
    float3 WorldPosition;
    float3 WorldNormal;
    float4 WorldTangent;
    float2 Texcoord;
    float4 VertexColor;
    float3 ViewVector;
    float ViewDepth;
    float Curvature;
};

/// Reads a morph delta out of the morph texture the same way the renderer packs it.
float3 SampleMorphDelta(float vertexId)
{
    float width = max(g_Instance.MorphParameters.y, 1.0);
    float height = max(g_Instance.MorphParameters.z, 1.0);

    float index = vertexId + g_Instance.MorphParameters.w;
    float column = fmod(index, width);
    float row = floor(index / width);

    float2 texcoord = (float2(column, row) + 0.5) / float2(width, height);
    float4 packed = g_tMorph.SampleLevel(texcoord, 0.0);

    return (packed.xyz * 2.0 - 1.0) * packed.w * g_Instance.MorphParameters.x;
}

float4x4 SkinMatrix(float4 weights, float4 indices)
{
    float4x4 result = g_Instance.BoneMatrices[int(indices.x)] * weights.x;
    result += g_Instance.BoneMatrices[int(indices.y)] * weights.y;
    result += g_Instance.BoneMatrices[int(indices.z)] * weights.z;
    result += g_Instance.BoneMatrices[int(indices.w)] * weights.w;
    return result;
}

[shader("vertex")]
Varyings vertexMain(VertexInput input)
{
    float3 objectPosition = input.Position + SampleMorphDelta(input.VertexId);

    float weightSum = dot(input.BlendWeights, float4(1.0));
    float4x4 skin = weightSum > 0.0
        ? SkinMatrix(input.BlendWeights / weightSum, input.BlendIndices)
        : g_Instance.ObjectToWorld;

    float4x4 objectToWorld = mul(g_Instance.ObjectToWorld, skin);

    float4 worldPosition = mul(objectToWorld, float4(objectPosition, 1.0));
    float3 worldNormal = normalize(mul((float3x3)objectToWorld, input.Normal));
    float3 worldTangent = normalize(mul((float3x3)objectToWorld, input.Tangent.xyz));

    Varyings output;
    output.ClipPosition = mul(g_View.WorldToProjection, worldPosition);
    output.WorldPosition = worldPosition.xyz;
    output.WorldNormal = worldNormal;
    output.WorldTangent = float4(worldTangent, input.Tangent.w);
    output.Texcoord = AnimateTexcoord(input.Texcoord, g_View.DepthRange.w);
    output.VertexColor = input.Color * g_Instance.TintColor;
    output.ViewVector = g_View.CameraPosition.xyz - worldPosition.xyz;
    output.ViewDepth = mul(g_View.WorldToView, worldPosition).z;

    // Cheap per-vertex curvature: how fast the normal turns per unit of world space.
    float3 neighbourNormal = normalize(mul((float3x3)objectToWorld, input.Normal + input.Tangent.xyz * 0.01));
    // The salt has to reach the output through a uniform, or constant folding would erase it and
    // every iteration would hand the driver a binary it has already seen.
    output.Curvature = length(neighbourNormal - worldNormal) * 100.0
        + g_Material.RenderMode.w * float(BENCH_SALT);

    return output;
}

// ---------------------------------------------------------------------------------------------
// Fragment stage
// ---------------------------------------------------------------------------------------------

Surface BuildSurface(Varyings input, float3x3 tangentFrame, LayerSample layer, float parallaxOcclusion)
{
    Surface surface;
    surface.WorldPosition = input.WorldPosition;
    surface.GeometricNormal = normalize(input.WorldNormal);
    surface.Normal = normalize(mul(layer.Normal, tangentFrame));
    surface.ViewDirection = normalize(input.ViewVector);
    surface.NdotV = max(dot(surface.Normal, surface.ViewDirection), 1e-4);

    surface.Albedo = SrgbToLinear(layer.Color.rgb) * input.VertexColor.rgb;
    surface.Opacity = layer.Color.a * input.VertexColor.a;

    surface.Roughness = clamp(layer.Material.r, MIN_ROUGHNESS, 1.0);
    surface.Metalness = saturate2(layer.Material.g);
    surface.AmbientOcclusion = saturate2(layer.Material.b * parallaxOcclusion);
    surface.Curvature = input.Curvature;

    surface.SpecularColor = lerp(float3(0.04), surface.Albedo, surface.Metalness);
    surface.SheenColor = g_Material.SheenTint.rgb * g_Material.SheenTint.a;
    surface.SubsurfaceColor = g_Material.SubsurfaceTint.rgb * g_Material.SubsurfaceTint.a;
    surface.SelfIllum = SrgbToLinear(g_tSelfIllum.Sample(input.Texcoord).rgb) * g_Material.SelfIllumTint.rgb;

    surface.ClearCoatWeight = saturate2(g_Material.ClearCoat.x);
    surface.ClearCoatRoughness = clamp(g_Material.ClearCoat.y, MIN_ROUGHNESS, 1.0);

    // Rotate the tangent basis and split the roughness into the two anisotropic axes.
    float rotation = g_Material.Anisotropy.y * 2.0 * PI;
    float2 rotated = float2(cos(rotation), sin(rotation));
    float3 tangentU = normalize(tangentFrame[0] * rotated.x + tangentFrame[1] * rotated.y);

    surface.TangentU = normalize(tangentU - surface.Normal * dot(surface.Normal, tangentU));
    surface.TangentV = cross(surface.Normal, surface.TangentU);

    float anisotropy = clamp(g_Material.Anisotropy.x, -0.99, 0.99);
    surface.RoughnessU = max(surface.Roughness * (1.0 + anisotropy), MIN_ROUGHNESS);
    surface.RoughnessV = max(surface.Roughness * (1.0 - anisotropy), MIN_ROUGHNESS);

    return surface;
}

[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    float3x3 tangentFrame = BuildTangentFrame(normalize(input.WorldNormal), input.WorldTangent);
    float3 tangentView = mul(tangentFrame, normalize(input.ViewVector));

    float2 texcoord = ParallaxOcclusion(input.Texcoord, tangentView, 0.0);

    LayerSample layer = BlendLayers(texcoord, input.VertexColor);
    ApplyDetail(layer, texcoord);

    float alpha = layer.Color.a * input.VertexColor.a;

    if (alpha < g_Material.AlphaParameters.x)
    {
        discard;
    }

    // A little noise on the ambient occlusion, standing in for the renderer's detail breakup.
    float breakup = FractalNoise(texcoord * 32.0, 4);
    float occlusion = lerp(1.0, breakup, saturate2(g_Material.DetailParameters.x));

    Surface surface = BuildSurface(input, tangentFrame, layer, occlusion);

    float3 color = EvaluateImageBasedLighting(surface);

    // Per-pixel rotation for the shadow kernel, so neighbouring pixels take different taps.
    float2 noise = Hash22(input.ClipPosition.xy);
    float angle = noise.x * 2.0 * PI;
    float2 rotation = float2(cos(angle), sin(angle));

    int lightCount = int(min(g_Lighting.LightCount.x, float(MAX_LIGHTS)));
    int litCount = 0;

    // Unrolling turns the light loop into MAX_LIGHTS copies of a heavy body, which is what makes a
    // shader expensive for the driver to link rather than expensive to parse.
#if UNROLL_LIGHTS
    [ForceUnroll]
#endif
    for (int i = 0; i < MAX_LIGHTS; i++)
    {
#if !UNROLL_LIGHTS
        if (i >= lightCount)
        {
            break;
        }
#endif

        Light light = g_Lighting.Lights[i];
        LightSample lightSample = EvaluateLight(light, surface.WorldPosition, surface.GeometricNormal, rotation, input.ViewDepth);

        if (lightSample.Attenuation <= 0.0)
        {
            continue;
        }

        float3 tangentLight = mul(tangentFrame, lightSample.Direction);
        float parallaxShadow = ParallaxShadow(texcoord, tangentLight, 0.0);

        color += ShadeDirect(surface, lightSample, parallaxShadow);
        litCount++;
    }

    // The sun is not in the light list, it comes out of the view constants.
    LightSample sun;
    sun.Color = g_View.SunColor.rgb * g_View.SunColor.a;
    sun.Direction = -normalize(g_View.SunDirection.xyz);
    sun.Attenuation = SampleSunShadow(surface.WorldPosition, surface.GeometricNormal, input.ViewDepth, rotation);

    float3 tangentSun = mul(tangentFrame, sun.Direction);
    color += ShadeDirect(surface, sun, ParallaxShadow(texcoord, tangentSun, 0.0));

    color += surface.SelfIllum;
    color = ApplyFog(color, surface.WorldPosition);
    color = TonemapToOutput(color);
    color = ApplyRenderMode(color, surface, litCount);

    // Keeps the salt live all the way to the output.
    color += g_Material.RenderMode.w * float(BENCH_SALT);

    return float4(color, alpha * g_Material.AlphaParameters.y);
}
