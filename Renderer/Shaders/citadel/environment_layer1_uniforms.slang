#version 460

#ifndef VRF_ENVIRONMENT_LAYER1_UNIFORMS_INCLUDE
#define VRF_ENVIRONMENT_LAYER1_UNIFORMS_INCLUDE

uniform sampler2D g_tColor1; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tNormalRoughness1;
uniform sampler2D g_tPacked1; // r=self illum, g=metalness

uniform mat4 g_mAlbedoColorCorrect1 = mat4(1.0);
uniform mat4 g_mTextureColorTint1 = mat4(1.0);

uniform bool g_bMaskColorTint1 = true;
uniform bool g_bMaskVertexColorTint1 = true;
uniform bool g_bModelTint1 = true;
uniform float g_fVertexColorStrength1 = 1.0;
uniform float g_fLayerNormalStrength1 = 1.0;
uniform float g_fLayerSelfIllumScale1 = 1.0;
uniform vec3 g_vLayerSelfIllumTint1 = vec3(1.0);
uniform vec2 g_vLayerRoughnessContrastBrightness1 = vec2(1.0, 0.0);
uniform vec2 g_vLayerTintMaskContrastBrightness1 = vec2(1.0, 0.0);
uniform vec2 g_vLayerMetalnessContrastBrightness1 = vec2(1.0, 0.0);

#if (F_OVERRIDE_BLOOM_AMOUNT == 1)
    uniform float g_flOverrideBloomAmount1 = 1.0;
#endif

#endif
