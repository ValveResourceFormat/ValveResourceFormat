#version 460

// Colors are picked in gamma space
const vec3 GridColor = vec3(0.8, 0.8, 0.85);
const vec3 XAxisColor = vec3(0.9, 0.2, 0.2);
const vec3 YAxisColor = vec3(0.2, 0.8, 0.2);

sample in vec3 nearPoint;
sample in vec3 farPoint;

out vec4 outputColor;

#include "common/utils.slang"
#include "common/view_constants.slang"

// Coverage of lines at multiples of cellSize. Lines are lineWidthWs world units wide, drawn 1 to maxWidthPx pixels
// wide; thinner lines lose alpha instead, so distant lines fade out rather than alias.
float GridLines(vec2 position, vec2 pixelSize, float cellSize, float lineWidthWs, float maxWidthPx)
{
    float lineWidth = lineWidthWs / cellSize;

    vec2 coord = position / cellSize;
    vec2 derivative = pixelSize / cellSize;
    vec2 drawWidth = clamp(vec2(lineWidth), derivative, derivative * maxWidthPx);
    vec2 distanceToLine = abs(fract(coord - 0.5) - 0.5);

    vec2 coverage = saturate((drawWidth * 0.5 - distanceToLine) / derivative + 0.5);
    coverage *= saturate(lineWidth / drawWidth);
    coverage = mix(coverage, vec2(lineWidth), saturate(derivative * 2.0 - 1.0));

    return max(coverage.x, coverage.y);
}

vec4 BlendOver(vec4 destination, vec3 color, float alpha)
{
    float outAlpha = alpha + destination.a * (1.0 - alpha);
    vec3 outColor = color * alpha + destination.rgb * destination.a * (1.0 - alpha);

    return vec4(outColor / max(outAlpha, 1e-6), outAlpha);
}

void main()
{
    vec3 ray = farPoint - nearPoint;
    float t = -nearPoint.z / ray.z;
    vec3 position = nearPoint + ray * t;
    vec4 clipSpacePosition = g_matViewToProjection * (g_matWorldToView * vec4(position, 1.0));
    float depth = clipSpacePosition.z / clipSpacePosition.w;

    // World units per pixel, taken before any discard since derivatives need the whole quad
    vec2 positionDx = dFdx(position.xy);
    vec2 positionDy = dFdy(position.xy);
    vec2 pixelSize = max(sqrt(positionDx * positionDx + positionDy * positionDy), vec2(1e-6));

    // Drop rays that miss the plane or hit it in front of the near plane
    bool hitsPlane = t > 0.0 && clipSpacePosition.w > 0.0 && depth <= 1.0 && !isnan(depth);

    if (!hitsPlane)
    {
        discard;
    }

    float gridAlpha = GridLines(position.xy, pixelSize, 64.0, 1.0, 1.0) * 0.45;
    gridAlpha = max(gridAlpha, GridLines(position.xy, pixelSize, 512.0, 4.0, 1.5) * 0.8);

    // Finer, fainter lines in the square around the origin
    bool isNearOrigin = all(lessThanEqual(abs(position.xy), vec2(128.0)));

    if (isNearOrigin)
    {
        gridAlpha = max(gridAlpha, GridLines(position.xy, pixelSize, 16.0, 1.0, 1.0) * 0.25);
        gridAlpha = max(gridAlpha, GridLines(position.xy, pixelSize, 4.0, 0.25, 1.0) * 0.08);
    }

    // The x = 0 line is the Y axis and the y = 0 line is the X axis
    vec2 axisCoverage = saturate(1.5 - abs(position.xy) / pixelSize);

    vec4 color = vec4(GridColor, gridAlpha);
    color = BlendOver(color, YAxisColor, axisCoverage.x);
    color = BlendOver(color, XAxisColor, axisCoverage.y);

    // Fade out toward the horizon
    float rayElevation = abs(ray.z) * inversesqrt(dot(ray, ray));
    color.a *= smoothstep(0.0, 0.3, rayElevation);

    if (color.a <= 0.0)
    {
        discard;
    }

    gl_FragDepth = gl_DepthRange.diff * depth + gl_DepthRange.near;
    outputColor = vec4(SrgbGammaToLinear(color.rgb), color.a);
}
