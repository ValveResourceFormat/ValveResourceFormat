#version 460

in vec2 vTexCoord;

out vec4 color;

uniform sampler2D uTexture;

void main() {
    color = texture(uTexture, vTexCoord);

    if (color.a < 0.002)
    {
        discard;
    }
}
