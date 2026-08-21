#version 460

// Shared by the sprite card renderers that expand their quads on the GPU. A layer's uv controls are one
// value per layer for the whole draw rather than per particle, so they arrive as uniforms and the
// particle only sends the sheet frame rectangle it is on.

// Scale in xy, offset in zw.
uniform vec4 uLayerUvScaleOffset[S_TEXTURE_LAYERS + 1];
uniform float uLayerUvRotation[S_TEXTURE_LAYERS + 1];
uniform bool uLayerUvClamp[S_TEXTURE_LAYERS + 1];

// Places one card corner in a layer's texture, matching ParticleTextureLayer.PlaceCorner: the card
// coordinate turns about the card centre, is divided by the scale, and lands at the wrapped offset;
// the result then maps into whichever sheet frame rect the layer is showing.
vec2 PlaceCorner(vec2 cardUv, int layer, vec2 rectMin, vec2 rectMax)
{
    vec2 centred = cardUv - vec2(0.5);

    float rotation = uLayerUvRotation[layer];
    float sinRotation = sin(rotation);
    float cosRotation = cos(rotation);

    vec2 rotated = vec2(
        (centred.x * cosRotation) - (centred.y * sinRotation),
        (centred.x * sinRotation) + (centred.y * cosRotation));

    vec4 scaleOffset = uLayerUvScaleOffset[layer];
    vec2 origin = scaleOffset.zw + vec2(0.5);
    vec2 wrapped = origin - floor(origin);

    vec2 placed = (rotated / scaleOffset.xy) + wrapped;

    if (uLayerUvClamp[layer])
    {
        placed = clamp(placed, 0.0, 1.0);
    }

    return rectMin + (placed * (rectMax - rectMin));
}
