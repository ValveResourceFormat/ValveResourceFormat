#version 460

#include "solidenergy_features.slang"
#include "common_ps.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec2 vDetail1CoordOut;
in vec2 vDetail2CoordOut;
in vec2 vSurfaceCoordOut;
in vec3 vVertexColorOut;

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tDetail1; // SrgbRead(true) Sampler(UserConfig)
uniform sampler2D g_tDetail2; // SrgbRead(true) Sampler(UserConfig)
uniform sampler2D g_tFlowmap; // Sampler(UserConfig)
uniform sampler2D g_tFlowNoise; // Sampler(UserConfig)
uniform sampler2D g_tFlowBounds; // Sampler(UserConfig)

uniform int F_DETAIL1;
uniform int F_DETAIL2;
uniform int g_nDetail1BlendMode;
uniform int g_nDetail2BlendMode;
uniform int F_FLOWMAP;
uniform int F_FLOW_CHEAP;
uniform int F_ADDITIVE_BLEND;

uniform float g_flOutputIntensity = 1.0;
uniform vec3 g_vFlowColor = vec3(0.1, 0.2, 0.4);
uniform float g_flFlowColorIntensity = 1.0;
uniform float g_flFlowWorldUvScale = 1.0;
uniform float g_flFlowNormalUvScale = 1.0;
uniform float g_flFlowTimeInterval = 0.4;
uniform float g_flFlowUvScrollDistance = 0.2;
uniform float g_flFlowNoiseScale = 0.0002;
uniform float g_flFlowLerpExponent = 0.0;

// Rises from zero at the camera to one at 40 units of depth, so that the energy does not blind the viewer up close
float GetNearFade(vec3 position)
{
    float depth = dot(position - g_vCameraPositionWs, g_vCameraDirWs);

    return smoothstep(0.0, 40.0, depth);
}

// Two layers of the base texture alpha scroll along the flow for one interval each, offset by half an interval, and
// each fades out where it restarts
vec3 GetFlowEnergy()
{
    vec4 bounds = texture(g_tFlowBounds, vTexCoordOut);
    float noise = texture(g_tFlowNoise, vSurfaceCoordOut * g_flFlowNoiseScale).y;

    float phase = g_flTime / (2.0 * g_flFlowTimeInterval) + noise;
    vec2 offsetA;
    vec2 offsetB;
    float progressA;
    float progressB;
    float weightA;
    float weightB;
    GetFlowLayers(phase, offsetA, offsetB, progressA, progressB, weightA, weightB);

    vec2 flow = texture(g_tFlowmap, vSurfaceCoordOut * g_flFlowWorldUvScale).xy * 2.0 - 1.0;
    flow *= bounds.x * g_flFlowUvScrollDistance;

    vec2 surfaceCoord = vSurfaceCoordOut * g_flFlowNormalUvScale;

    vec2 coordA = surfaceCoord + offsetA + flow * (progressA - 0.5);
    vec2 coordB = surfaceCoord + offsetB + flow * (progressB - 0.5);

    if (F_FLOW_CHEAP != 0)
    {
        // Without the flow map the layers stay in place and fade by a square
        coordA = surfaceCoord + vec2(0.5);
        coordB = surfaceCoord;
        weightA *= weightA;
        weightB *= weightB;
    }
    else
    {
        weightA = pow(weightA, g_flFlowLerpExponent);
        weightB = pow(weightB, g_flFlowLerpExponent);
    }

    float layerA = texture(g_tColor, coordA).a;
    float layerB = texture(g_tColor, coordB).a;
    float ripples = layerA * weightA + layerB * weightB;

    return (ripples + bounds.y) * g_vFlowColor * (bounds.z * g_flFlowColorIntensity);
}

// The base texture combined with the detail layers. A detail of blend mode 0 multiplies and brightens the base, one of
// blend mode 1 lays the base over it by the base alpha. A second detail adds the first or multiplies it. The vertex
// color is raised to the returned power.
vec3 GetLayeredEnergy(vec4 baseColor, out float vertexColorExponent)
{
    float brightness = baseColor.a + 1.0;
    vertexColorExponent = 2.0 * (2.0 - baseColor.a);
    vec3 color = baseColor.rgb;

    if (F_DETAIL1 == 0)
    {
        return color * brightness;
    }

    vec3 detail1 = texture(g_tDetail1, vDetail1CoordOut).rgb;

    if (g_nDetail1BlendMode == 0)
    {
        color = 2.0 * baseColor.rgb * detail1;
    }
    else
    {
        brightness = 1.0;
        vertexColorExponent = 2.0;
        color = baseColor.rgb * mix(detail1, vec3(1.0), baseColor.a);
    }

    if (F_DETAIL2 != 0)
    {
        vec3 detail2 = texture(g_tDetail2, vDetail2CoordOut).rgb;

        if (g_nDetail2BlendMode == 0)
        {
            color += detail2 * detail1;
        }
        else
        {
            color *= detail2;
        }
    }

    return color * brightness;
}

void main()
{
    vec4 baseColor = texture(g_tColor, vTexCoordOut);
    vec3 energy;

    if (F_FLOWMAP != 0)
    {
        energy = GetFlowEnergy();
    }
    else
    {
        float vertexColorExponent;
        energy = GetLayeredEnergy(baseColor, vertexColorExponent);

#if (F_VERTEX_COLOR == 1)
        energy *= pow(vVertexColorOut, vec3(vertexColorExponent));
#endif
    }

    energy *= GetNearFade(vFragPosition);

    // The alpha of the blend is the intensity, so the color of an additive surface is scaled by it twice
    vec3 color = energy * g_flOutputIntensity;

    if (F_ADDITIVE_BLEND != 0)
    {
        color *= GetFogAttenuation(vFragPosition);
    }
    else
    {
        ApplyFog(color, vFragPosition);
    }

    outputColor = vec4(color, g_flOutputIntensity);
}
