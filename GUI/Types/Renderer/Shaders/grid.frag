#version 460

// https://github.com/BabylonJS/Babylon.js/blob/bd7351cfc97884d3293d5858b8a0190cda640b2f/packages/dev/materials/src/grid/grid.fragment.fx

precision highp float;

#define SQRT2 1.41421356
#define PI 3.14159

in vec3 vtxPosition;
out vec4 outputColor;

float getDynamicVisibility(float position) {
    // Major grid line every frequency
    float majorGridFrequency = 10;

    if (floor(position + 0.5) == floor(position / majorGridFrequency + 0.5) * majorGridFrequency) {
        return 1.0;
    }

    return 0.1;
}

float getAnisotropicAttenuation(float differentialLength) {
    const float maxNumberOfLines = 10.0;
    return clamp(1.0 / (differentialLength + 1.0) - 1.0 / maxNumberOfLines, 0.0, 1.0);
}

float isPointOnLine(float position, float differentialLength) {
    float fractionPartOfPosition = position - floor(position + 0.5); // fract part around unit [-0.5; 0.5]
    fractionPartOfPosition /= differentialLength; // adapt to the screen space size it takes
    fractionPartOfPosition = clamp(fractionPartOfPosition, -1.0, 1.0);

    float result = 0.5 + 0.5 * cos(fractionPartOfPosition * PI); // Convert to 0-1 for antialiasing.
    return result;
}

float contributionOnAxis(float position) {
    float differentialLength = length(vec2(dFdx(position), dFdy(position)));
    differentialLength *= SQRT2; // Multiply by SQRT2 for diagonal length

    // Is the point on the line.
    float result = isPointOnLine(position, differentialLength);

    // Add dynamic visibility.
    float dynamicVisibility = getDynamicVisibility(position);
    result *= dynamicVisibility;

    // Anisotropic filtering.
    float anisotropicAttenuation = getAnisotropicAttenuation(differentialLength);
    result *= anisotropicAttenuation;

    return result;
}

float RemapVal( float flOldVal, float flOldMin, float flOldMax, float flNewMin, float flNewMax )
{
	// Put the old val into 0-1 range based on the old min/max
	float flValNormalized = ( flOldVal - flOldMin ) / ( flOldMax - flOldMin );

	// Map 0-1 range into new min/max
	return ( flValNormalized * ( flNewMax - flNewMin ) ) + flNewMin;
}

void main(void) {
    // Scale position to the requested ratio.
    vec3 gridPos = vtxPosition / 2;

    // Find the contribution of each coords.
    float x = contributionOnAxis(gridPos.x);
    float y = contributionOnAxis(gridPos.y);

    // Create the grid value from the max axis.
    float opacity = max(x, y);

    // Apply the color.
    vec3 lineColor = vec3(1.0, 1.0, 1.0);

    float px = floor(gridPos.x + 0.5);
    float py = floor(gridPos.y - 0.05);

    if (floor(gridPos.y + 0.5) == 0.0 && gridPos.x >= 0.0) {
        lineColor = vec3(1.0, 0.0, 0.0);
    }

    if (floor(gridPos.x + 0.5) == 0.0 && gridPos.y >= 0.0) {
        lineColor = vec3(0.0, 1.0, 0.0);
    }

    outputColor = vec4(lineColor, opacity);
}
