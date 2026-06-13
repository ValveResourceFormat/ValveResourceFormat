#version 460

in vec3 vSkyLookupInterpolant;
out vec4 outputColor;

#include "common/utils.slang"
#include "common/ViewConstants.slang"

uniform bool g_bShowLightBackground = false;
uniform bool g_bShowSolidBackground = false;

#define PIh (PI / 2.0)
#define PI2t (TAU / 3.0)

// how many pillars are there
#define PILLARS 3.0

// polar coordinates from
// lat: 0...PI2
// lon: 0...PI
vec2 polarCoords(in vec3 xyz) {
    // it may be necessary in your projection to either:
    // a) rotate the projection by 90deg OR
    // b) swap xyz until the projection makes sense:
    float theta = atan(xyz.y, xyz.x);
    float rho = sqrt(pow(xyz.x, 2.0) + pow(xyz.y, 2.0) + pow(xyz.z, 2.0));
    return vec2((theta + PIh) * 2.0, acos(xyz.z / rho));
}

// goes from 0...1:
// - is 1 when forward along x axes
// - is 0 when forward along y axes
// (with multiples by the amount of color changing pillars)
float latitude(float polar) {
    return (sin(polar * PILLARS / 2.0) + 1.0) / 2.0;
}

vec3 Rgb(int r, int g, int b)
{
    return vec3(r, g, b) / 255.0;
}

void main() {
    if (g_bShowSolidBackground) {
        outputColor = g_bShowLightBackground ? vec4(0.0, 1.0, 0.0, 1.0) : vec4(1.0, 0.0, 1.0, 1.0);
        return;
    }

    // polar coordinates in lat/lon
    vec2 polar = polarCoords(vSkyLookupInterpolant.xyz);

    // goes from 0...1 from bottom to up
    // if the sphere is upside down:
    // - do 1.0 - polar.y / PI
    float upAmount = 1.0 - polar.y / PI;

    // interpolation values to move from one phase to another
    // steeper transitions with higher powers
    float ground = pow(smoothstep(0.1, 0.3, upAmount), 2.0);
    float horizon = pow(smoothstep(0.3, 0.5, upAmount), 12.0);
    float sky = pow(smoothstep(0.45, 0.6, upAmount), 2.0);
    float highSky = pow(smoothstep(0.6, 0.85, upAmount), 3.0);

    vec3 vApp = Rgb(44, 49, 61);

    vec3 SKY_C = Rgb(158, 158, 158);
    vec3 SKY_ALT_C = Rgb(75, 75, 75);
    vec3 HORIZON_C = Rgb(55, 55, 55);
    vec3 HORIZON_ALT_C = Rgb(75, 75, 75);
    vec3 GROUND_C = Rgb(43, 43, 43);
    vec3 GROUND_ALT_C = Rgb(54, 50, 54);

    if (!g_bShowLightBackground)
    {
        SKY_C = vApp * 3;
        SKY_ALT_C = vApp * 2;
        HORIZON_C = vApp * 1;
        HORIZON_ALT_C = vApp * 1.2;
        GROUND_C = vApp * 0.8;
        GROUND_ALT_C = vApp * 0.9;
    }

    // mixing the alternative colors to go through their alt version
    // set the PI2t multication to vary the color phases
    vec3 skyColor = mix(SKY_C, SKY_ALT_C, latitude(polar.x + 0.0 * PI2t));
    vec3 groundColor = mix(GROUND_C, GROUND_ALT_C, latitude(polar.x + 1.0 * PI2t));
    vec3 horizonColor = mix(HORIZON_C, HORIZON_ALT_C, latitude(polar.x + 2.0 * PI2t));

    vec3 lowGroundToGround = mix(GROUND_C, groundColor, ground);
    vec3 groundToHorizon =  mix(lowGroundToGround, horizonColor, horizon);
    vec3 horizonToSky = mix(groundToHorizon, skyColor, sky);
    vec3 skyToHighSky = mix(horizonToSky, SKY_C, highSky);

    outputColor = vec4(SrgbGammaToLinear(skyToHighSky), 1.0);
}
