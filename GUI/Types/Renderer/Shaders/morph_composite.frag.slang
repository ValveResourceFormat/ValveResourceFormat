#version 460

uniform sampler2D morphAtlas;

in vec4 offset;
in vec4 range;
in vec2 morphState;
in vec2 texCoords;

out vec4 outputColor;

void main()
{
    vec2 targetTexCoords = vec2(texCoords.x, texCoords.y);
    vec4 pixel = (texture(morphAtlas, targetTexCoords) * range) + offset;
    float alpha = pixel.w;
    outputColor = vec4(pixel.xyz * morphState.x, 0.0);
}
