#version 330

in vec2 vTexCoordOut;

uniform sampler2D g_tColor;

out vec4 outputColor;

void main(void) {
    outputColor = texture2D(g_tColor, vTexCoordOut);
}
