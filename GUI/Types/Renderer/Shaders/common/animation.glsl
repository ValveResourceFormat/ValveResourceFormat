//? #version 460

layout (location = 1) in vec4 vBLENDINDICES;
layout (location = 2) in vec4 vBLENDWEIGHT;

uniform float bAnimated = 0;
uniform float fNumBones = 1;
uniform sampler2D animationTexture;

mat4 getMatrix(float id) {
    float texelPos = id/fNumBones;
    return mat4(texture(animationTexture, vec2(0.00, texelPos)),
        texture(animationTexture, vec2(0.25, texelPos)),
        texture(animationTexture, vec2(0.50, texelPos)),
        texture(animationTexture, vec2(0.75, texelPos)));
}

mat4 getSkinMatrix() {
    // Calculate animation matrix
    mat4 skinMatrix = mat4(1.0 - bAnimated);
    skinMatrix += bAnimated * vBLENDWEIGHT.x * getMatrix(vBLENDINDICES.x);
    skinMatrix += bAnimated * vBLENDWEIGHT.y * getMatrix(vBLENDINDICES.y);
    skinMatrix += bAnimated * vBLENDWEIGHT.z * getMatrix(vBLENDINDICES.z);
    skinMatrix += bAnimated * vBLENDWEIGHT.w * getMatrix(vBLENDINDICES.w);
    return skinMatrix;
}
