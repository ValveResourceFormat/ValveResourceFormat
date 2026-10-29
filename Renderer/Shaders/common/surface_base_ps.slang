#version 460

#ifndef VRF_SURFACE_BASE_PS_INCLUDE
#define VRF_SURFACE_BASE_PS_INCLUDE

// Inputs, outputs and lighting of a lit surface fragment shader.
// The shader declares its combos and the lighting profile knobs it sets (BARN_LIGHT_*, LIGHTMAP_VR_SHADING, ...) first,
// includes this, then declares its samplers and uniforms and writes GetMaterial() and main() from the helpers of
// surface_material.slang and surface_shading.slang.
// The shared files test the optional combos in preprocessor conditions only, where a combo the shader does not
// ship counts as 0. A shader that cuts out by a blend mode sets ALPHA_TEST_BY_BLEND_MODE to whether it does.
// The depth and overdraw passes shade nothing, so they leave out the fog and the lighting.

#include "utils.slang"
#include "shared_combos.slang"
#include "view_constants.slang"
#include "lighting_constants.slang"

// 1 is the depth only pass, 2 the depth pass that also counts the quad overdraw
#define S_MODE_DEPTH 0

uniform int F_DISABLE_TONE_MAPPING;
uniform int F_SCALE_NORMAL_MAP;
uniform int F_NO_SPECULAR_AT_FULL_ROUGHNESS;
uniform int F_SSS_MASK;
uniform float g_flNormalMapScaleFactor = 1.0;
uniform int g_bFirstpersonLegsDistortion = 0;

in vec3 vFragPosition;
in vec3 vNormalOut;
centroid in vec3 vCentroidNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;
centroid in vec4 vVertexColorOut;

#if (F_USE_PER_VERTEX_CURVATURE == 1)
    in float vCurvatureOut;
#endif

#if (F_SECONDARY_UV == 1)
    in vec2 vTexCoord2;
#endif

// Depth only passes of materials without alpha test have no color output
#if (S_MODE_DEPTH != 1) || (F_ALPHA_TEST == 1) || (ALPHA_TEST_BY_BLEND_MODE == 1)
    out vec4 outputColor;
#endif

#if (S_MODE_DEPTH == 2) || ((S_MODE_DEPTH == 1) && (F_ALPHA_TEST == 0) && (ALPHA_TEST_BY_BLEND_MODE == 0))
    layout (early_fragment_tests) in;
#endif

#if (S_MODE_DEPTH == 2)
    #include "quad_overdraw.slang"
#endif

uniform sampler2D g_tBlueNoise; // reserved

#if (F_ANISOTROPIC_GLOSS == 1)
    uniform sampler2D g_tAnisoGloss;
#endif

#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    uniform int F_USE_ALBEDO_FOR_TRANSMISSIVE = 0;
    uniform sampler2D g_tTransmissiveColor; // SrgbRead(true)
#endif

#include "fullbright.slang"
#include "texturing.slang"

#if (S_MODE_DEPTH == 0)
    #include "fog.slang"

    // Must be last
    #include "lighting.slang"
#endif

#endif
