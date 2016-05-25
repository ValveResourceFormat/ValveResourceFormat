#version 330

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tFlow;
uniform sampler2D g_tNormal;
uniform sampler2D g_tNoise;

uniform vec3 vLightPosition;

uniform vec4 m_vTintColorSceneObject;
uniform vec3 m_vTintColorDrawCall;

//Main entry point
void main()
{
    outputColor = 0.5 * texture2D(g_tNormal, vTexCoordOut) * texture2D(g_tNoise, vTexCoordOut);
}
