#version 460

#ifndef VRF_WETNESS_INCLUDE
#define VRF_WETNESS_INCLUDE

#include "utils.slang"
#include "ViewConstants.slang"
#include "texturing.slang"

// Whether this material responds to weather at all.
uniform int F_WETNESS = 0;

// Where water collects. Puddles fill anything the height map puts below the water level, and the
// wet edge is the damp ring that spreads out around them.
uniform float g_fPuddleStrength = 1.0;
uniform float g_fPuddleRoughness = 0.02;
uniform float g_fPuddleBlendSoftness = 0.08;
uniform float g_fWetEdgeStrength = 0.5;
uniform float g_fWetEdgeSpread = 0.2;
uniform bool g_bPuddlesOnVerticalSurfaces = false;

// Silt left in the bottom of a puddle.
uniform vec3 g_vPuddleSedimentColor = vec3(0.62, 0.56, 0.5); // SrgbRead(true)
uniform float g_fPuddleSedimentHeight = 0.9;
uniform float g_fPuddleSedimentOpacity = 0.25;

// Sheen over everything else, independent of whether water pooled.
uniform float g_fWetnessStrength = 1.0;
uniform float g_flWetnessDarkeningStrength1 = 1.0;

// Animated water. Both are scaled by the weather's own strength, so a material opting in still
// shows nothing until it rains.
uniform float g_fRainStrength = 1.0;
uniform float g_fRippleStrength = 1.0;

// Clamps the height map to positive before any of it is read, for materials whose height map is
// authored signed around its zero point.
uniform bool g_bWetnessUseHeightmapAdjustments = false;

// Global weather state
#define g_flEnvWetnessCoverage g_vEnvWetness.x
#define g_flEnvWetnessDryingAmount g_vEnvWetness.y
#define g_flEnvRainStrength g_vEnvWetness.z
#define g_flEnvPuddleRippleStrength g_vEnvWetness.w
#define g_flEnvPuddleRippleDirection g_vEnvWetnessRipple.x

uniform sampler2D g_tWetnessWaves; // reserved, materials/dev/water_waves.vtex

// Filled by ApplyWetness: how much standing water is on this fragment, for PuddleWaterDiffuse
// at the bottom of this file. Zero means no water, which is the identity for that term.
float g_flPuddleWaterScattering = 0.0;

// Surface derived ripple normal, in world space, plus the roughness the ripples add.
// Two scrolling samples of the same wave normal map, each projected onto a flow direction that
// the weather can rotate. Both are damped once a sample is small enough on screen to alias.
vec3 GetWaterRipples(vec3 posWS, vec3 geometricNormal, float wetPaint, float strength, out float roughnessAdd,
    out float foam)
{
    vec3 flowDir = normalize(vec3(-1.0, 0.2, 0.0));

    float angle = g_flEnvPuddleRippleDirection * 6.283185482025146484375;
    float sinAngle = sin(angle);
    float cosAngle = cos(angle);
    flowDir.xy = flowDir.xy * mat2(vec2(cosAngle, -sinAngle), vec2(sinAngle, cosAngle));

    vec3 flowSide = -cross(flowDir, geometricNormal);
    float alongSide = dot(posWS, flowSide);

    // Drier paint drags the pattern a long way along the flow, so the ripples do not line up
    // across a coverage boundary.
    float dryness = 1.0 - wetPaint;
    vec2 phase = vec2(dot(posWS, flowDir))
        + vec2(0.1, -0.1) * alongSide
        + vec2(8.0, 5.0) * posWS.z
        + vec2(dryness * dryness * -80.0);

    vec2 coarseUv = vec2(phase.x, alongSide) * vec2(0.02, 0.011);
    vec4 coarse = texture(g_tWetnessWaves, coarseUv + vec2(0.2, 0.0) * g_flTime);
    vec2 coarseNormal = coarse.xy * 2.0 - 1.0;
    float coarseBlur = saturate(length(fwidth(coarseUv)) * 10.0);
    vec2 coarseOffset = coarseNormal * vec2(0.1, 0.05) * wetPaint * mix(1.0, 0.3, coarseBlur);

    // The second layer is steered by the first, which is what stops the two from beating against
    // each other into a visible grid.
    vec2 fineUv = vec2(phase.y, alongSide) * vec2(0.036, 0.0162) + coarseNormal * 0.05;
    vec4 fine = texture(g_tWetnessWaves, fineUv + vec2(0.15, 0.0) * g_flTime);
    vec2 fineNormal = (fine.xy * 2.0 - 1.0) * 0.052 * wetPaint;
    fineNormal.y *= 0.5;
    float fineBlur = saturate(length(fwidth(fineUv)) * 10.0);
    vec2 fineOffset = fineNormal * mix(1.0, 0.3, fineBlur);

    vec2 offset = vec2(dot(flowDir.xy, coarseOffset), dot(flowSide.xy, coarseOffset))
        + vec2(dot(flowDir.xy, fineOffset), dot(flowSide.xy, fineOffset));

    // What the ripples cost in roughness is what they lost to the blur above, so a ripple field
    // that has gone too small to resolve turns into a rough sheen instead of shimmering.
    roughnessAdd = strength * 0.04 * (coarseBlur + fineBlur);

    // Where the two wave layers pile up. Only the scattering term below reads this.
    foam = wetPaint * (coarse.z + fine.z);

    return vec3(offset * strength, 0.0);
}

// Rain landing on standing water: three rotated layers of a procedural cell pattern, each cell
// holding one expanding ring. Returns the normal perturbation in xy and a sparkle in z.
// There is no texture behind this; the cells come from fract() on a rotated grid.
vec3 GetRainImpacts(vec3 posWS, float height, float puddle, float strength)
{
    const vec3 rotCos = vec3(0.9713, 0.7518, 0.3624);
    const vec3 rotSin = vec3(0.2377, 0.6594, 0.9320);
    const vec3 layerScale = vec3(0.6, 0.6661, 0.7322);

    vec2 base = posWS.xy * 0.12;
    vec3 layerTime = vec3(g_flTime * 6.5) + vec3(0.0, 0.3167, 0.6334);
    vec3 accum = vec3(0.0);

    float strengthSq = strength * strength;

    for (int i = 0; i < 3; i++)
    {
        mat2 rot = mat2(vec2(rotCos[i], -rotSin[i]), vec2(rotSin[i], rotCos[i]));

        vec2 uv = ((rot * base) * layerScale[i]) + vec2(layerTime[i] * 0.02);
        vec2 cell = rot * ((fract(uv) * 2.0) - vec2(1.0));
        float dist = length(cell);

        // A puddle spreads the ring out; bare ground gets a tight splash.
        float ring = saturate(1.0 - (dist * mix(3.0, 1.0, puddle)));
        float core = saturate(1.0 - (dist * 6.0));

        // Offsetting each cell's clock by its grid coordinate is what keeps the three layers
        // from pulsing in step.
        float cellX = floor(fract(uv.x * 0.1) * 10.0);
        float cellY = floor(fract(uv.y * 0.1) * 10.0);
        float cellTime = layerTime[i] + ((cellX + (cellY * (0.2 + cellX))) * 8.0);
        layerTime[i] = cellTime;

        float ringPulse = saturate((sin(((ring * mix(1.0, 4.0, puddle)) + cellTime) * strength)
            - (1.0 - ((0.2 * strengthSq) * mix(0.25, 1.0, puddle)))) * 2.0) * ring / strengthSq;

        vec2 outward = (cell / vec2(dist)) * cos(mix(1.0, (ring * 40.0) + (g_flTime * 65.0), puddle));
        vec2 wave = -((outward * ring * ring) * mix(2.0, 0.25, saturate(fwidth(ring) * 20.0)));

        accum.xy += wave * ringPulse * mix(16.0, 2.0, puddle);

        // The height map feeds the sparkle's phase so the bright specks do not all flash at once
        // across a flat surface.
        accum.z += (saturate((sin((((core + cellTime) + 4.0) + (sin(height * 60.0) * 0.25)) * strength)
            - (1.0 - (strengthSq * 0.02))) * 2.0) * core / strengthSq) * 28.0;
    }

    return accum;
}

// Applies the whole effect in place. rawHeight is the height map before its zero point is taken
// off, wetPaint and paintSoftness are the blend value stream's z and w, and reflectance is the
// dielectric F0 the caller is about to build its specular colour from.
void ApplyWetness(inout MaterialProperties_t mat, float rawHeight, float wetPaint, float paintSoftness,
    inout float reflectance)
{
    if (F_WETNESS != 1 || (g_fPuddleStrength + g_fWetnessStrength) <= 0.0 || wetPaint <= 0.0)
    {
        return;
    }

    vec3 posWS = mat.PositionWS;
    vec3 geometricNormal = mat.GeometricNormal;
    float height = g_bWetnessUseHeightmapAdjustments ? max(0.0, rawHeight) : rawHeight;

    // Sharp enough that only a nearly level surface counts as level.
    float upFacing = pow(max(0.0, geometricNormal.z), 32.0);

    vec4 waves = texture(g_tWetnessWaves, posWS.xy * 0.006);

    // How wet this fragment is allowed to get: the paint, what the weather is pouring on, and
    // what the drying pass has taken back off. Drying only bites past halfway.
    float coverage = min(min(wetPaint, saturate(g_flEnvWetnessCoverage)),
        1.0 - (saturate(g_flEnvWetnessDryingAmount - 0.5) * 2.0));

    // Past three quarters the water level climbs sharply, so the last of the coverage range
    // floods rather than creeping.
    float overflow = saturate(coverage - 0.75);
    float waterLevel = coverage + (4.0 * overflow * overflow);
    float edgeLevel = waterLevel - g_fWetEdgeSpread;
    float fadeIn = saturate(waterLevel * 20.0);

    vec3 puddleNormal;
    float puddleLevel;

    if (g_bPuddlesOnVerticalSurfaces)
    {
        // Water clings to whatever it is on rather than lying flat.
        puddleNormal = geometricNormal;
        puddleLevel = max(0.0, edgeLevel);
    }
    else
    {
        puddleNormal = vec3(0.0, 0.0, 1.0);
        puddleLevel = max(0.0, (waterLevel * upFacing) - g_fWetEdgeSpread);
    }

    // Inverted smoothstep: the surface is under water where the height map is below the level.
    float softness = max(0.001, g_fPuddleBlendSoftness);
    float puddle = smoothstep(puddleLevel + softness, puddleLevel - (paintSoftness + (softness * 2.0)), height) * fadeIn;

    float wetEdge = max(smoothstep(coverage, edgeLevel - (0.2 * waterLevel), height) * g_fWetEdgeStrength * fadeIn,
        puddle) * g_fPuddleStrength;

    // Negative, and more so the deeper under the water line the fragment sits.
    float depth = (coverage - 1.0) - height;
    float sedimentHeight = (g_fPuddleSedimentHeight * 2.0) - 1.0;
    float sediment = saturate(depth + sedimentHeight) * g_fPuddleSedimentOpacity * puddle;

    // The sheen, blotched by the wave texture's alpha so it is not a flat wash, and never less
    // than the puddles already are.
    float wetness = saturate((saturate((((waterLevel * 2.0)
        - (mix(0.5, waves.w, saturate(upFacing * 20.0)) * 0.5))
        - (height * 0.5)) + 0.25) * g_fWetnessStrength * fadeIn) + puddle);

    vec3 waterNormal = puddleNormal;
    float rippleRoughness = 0.0;
    float rainSparkle = 0.0;
    float foam = 0.0;

    // Only surfaces that read as properly wet animate.
    if (wetness > 0.5)
    {
        float rippleStrength = g_fRippleStrength * g_flEnvPuddleRippleStrength;

        if (rippleStrength > 0.0)
        {
            waterNormal += GetWaterRipples(posWS, geometricNormal, wetPaint, rippleStrength, rippleRoughness, foam)
                * upFacing;
        }

        float rainStrength = (g_fRainStrength * g_flEnvRainStrength) * g_flEnvRainStrength;

        if (rainStrength > 0.0)
        {
            vec3 impacts = GetRainImpacts(posWS, height, puddle, rainStrength) * upFacing;

            waterNormal.xy += impacts.xy;
            rainSparkle = impacts.z;
        }

        waterNormal = normalize(waterNormal);
    }

    float puddleAmount = puddle * g_fPuddleStrength;
    vec3 sedimentMix = vec3(wetEdge * sediment);

    vec3 wetNormal = normalize(mix(mat.Normal, waterNormal, saturate(puddleAmount * 4.0)));

    // What the specular actually responds to. Drying kills the sheen twice as fast as it kills
    // the coverage, so a surface stops looking glossy well before the puddles have gone.
    float wetAmount = max(wetness * (1.0 - saturate(g_flEnvWetnessDryingAmount * 2.0)), wetEdge);

    // Wet things go darker and a little more saturated, and rough things go darkest, because a
    // rough surface holds the water that fills its texture in.
    vec3 darkened = mix(mat.Albedo, pow(mat.Albedo, vec3(1.3)) * 0.8,
        saturate((pow(mat.Albedo, vec3(0.25)) * (wetAmount * mat.RoughnessTex.x)) * 8.0) * g_flWetnessDarkeningStrength1);

    // Standing water halves it again, then the silt is laid over the top. The occlusion is
    // folded into the albedo here and taken back out of the occlusion channel below, which is
    // what stops a puddle from reading as a hole.
    vec3 pooled = (mix(darkened, darkened * 0.5, vec3(saturate(puddle * 1.5))) * mix(1.0, mat.AmbientOcclusion, wetEdge))
        * mix(1.0, 1.0 - depth, sediment);

    mat.Albedo = mix(pooled, g_vPuddleSedimentColor, sedimentMix) * (1.0 + rainSparkle) + (rainSparkle * 0.1);

    // The fwidth term is the same anti aliasing idea as the ripple blur: a normal that is
    // changing fast across the pixel becomes roughness instead of noise.
    mat.RoughnessTex = saturate(mix(mat.RoughnessTex, vec2(g_fPuddleRoughness), vec2(wetAmount))
        + vec2(rippleRoughness + (length(fwidth(wetNormal)) * 0.5)));

    mat.AmbientOcclusion = mix(mat.AmbientOcclusion, 1.0, wetEdge);

    // Water is a weaker dielectric than most of what it sits on.
    reflectance = mix(reflectance, 0.035, wetAmount);

    // Only the silt drags the ambient normal toward the water, and it drags the surface normal
    // as it was before the ripples, so the diffuse keeps reading the ground underneath while the
    // specular follows the water.
    mat.AmbientNormal = mix(mat.Normal, waterNormal, sedimentMix);
    mat.Normal = wetNormal;

    // Standing water deep enough to scatter light, thickened where the waves piled up.
    g_flPuddleWaterScattering = puddleAmount * (0.2 + (0.15 * foam * foam * foam));
}

// NOT WIRED UP. Replaces one light's NdotL where standing water should scatter it rather than
// reflect it off a hard surface: a wrapped lambert with a Fresnel rim, gated so it only shows on
// the lit side, and darkened by how far the water's own normal has turned away from the light.
//
// Use in common/lighting.slang, per light, as the NdotL fed to the diffuse term:
//   NdotL = PuddleWaterDiffuse(NdotL, mat.AmbientNormal, mat.Normal, lightDir, g_flPuddleWaterScattering);
//
// NdotL is the usual max(0, dot(mat.AmbientNormal, lightDir)); surfaceNormal and waterNormal are
// the ambient and full normals ApplyWetness left behind. The 10.0 extrapolation past the water
// normal is Valve's and is deliberately not normalized.
float PuddleWaterDiffuse(float NdotL, vec3 surfaceNormal, vec3 waterNormal, vec3 lightDir, float scattering)
{
    if (scattering <= 0.0)
    {
        return NdotL;
    }

    float amount = saturate(scattering);
    float waterNdotL = dot(waterNormal, lightDir);

    float wrapped = (0.5 + (NdotL * 0.5)) + pow(1.0 - saturate(waterNdotL), 4.0);
    float lit = saturate((waterNdotL + 0.2) * 4.0);
    float shadowed = saturate(mix(dot(mix(surfaceNormal, waterNormal, vec3(10.0)), lightDir), 1.0, amount));

    return mix(NdotL, (wrapped * lit) * shadowed, amount);
}

#endif
