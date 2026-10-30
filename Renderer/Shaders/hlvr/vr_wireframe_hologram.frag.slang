#version 460

// The fill of the hologram: a fresnel glow on the faces that look at the camera, fading out with distance.
// The wireframe lines are drawn by a geometry stage that this renderer does not have, so they are not drawn.
// The fog only fades the opacity of the hologram and is not applied.

#include "../common/utils.slang"
#include "../common/view_constants.slang"

in vec3 vFragPosition;
in vec3 vNormalOut;

out vec4 outputColor;

uniform vec3 g_vHologramColor = vec3(0.4, 0.7, 1.0);
uniform float g_flFresnelExponent = 5.0;
uniform float g_flFadeStartDistance = 45.0;
uniform float g_flFadeEndDistance = 90.0;
uniform float g_flLineWidth = 1.0; // Read by the geometry stage that draws the lines

void main()
{
    vec3 toPixel = vFragPosition - g_vCameraPositionWs;

    float fade = 1.0 - saturate((length(toPixel) - g_flFadeStartDistance) / (g_flFadeEndDistance - g_flFadeStartDistance));
    float facing = saturate(-dot(normalize(vNormalOut), normalize(toPixel)));
    float fresnel = pow(saturate(1.0 - facing), g_flFresnelExponent);

    outputColor = vec4(vec3(fresnel) * (g_vHologramColor * fade), 0.2);
}
