#version 330

in vec3 vSkyLookupInterpolant;
out vec4 vColor;

uniform samplerCube g_tSkyTexture;

void main()
{
    vec3 vEyeToSkyDirWs = normalize(vSkyLookupInterpolant);
    vColor = vec4(texture(g_tSkyTexture, vEyeToSkyDirWs).rgb + (vSkyLookupInterpolant*0.0), 1.0);
}
