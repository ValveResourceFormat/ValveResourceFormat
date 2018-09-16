#version 330

in vec3 vPOSITION;
in vec2 vTEXCOORD;
in vec4 vBLENDINDICES;
in vec4 vBLENDWEIGHT;

out vec3 vFragPosition;

out vec2 vTexCoordOut;

uniform mat4 projection;
uniform mat4 modelview;
uniform mat4 transform;

uniform float bAnimated = 0;
uniform float fNumBones = 1;
uniform sampler2D animationTexture;

mat4 getMatrix(float id) {
    float texelPos = id/fNumBones;
    return mat4(texture2D(animationTexture, vec2(0.00, texelPos)),
        texture2D(animationTexture, vec2(0.25, texelPos)),
        texture2D(animationTexture, vec2(0.50, texelPos)),
        texture2D(animationTexture, vec2(0.75, texelPos)));
}

void main()
{
    // Calculate animation matrix
	mat4 skinMatrix = mat4(1.0 - bAnimated);
    skinMatrix += bAnimated * vBLENDWEIGHT.x * getMatrix(vBLENDINDICES.x);
    skinMatrix += bAnimated * vBLENDWEIGHT.y * getMatrix(vBLENDINDICES.y);
    skinMatrix += bAnimated * vBLENDWEIGHT.z * getMatrix(vBLENDINDICES.z);
    skinMatrix += bAnimated * vBLENDWEIGHT.w * getMatrix(vBLENDINDICES.w);

	gl_Position = projection * modelview * transform * skinMatrix * vec4(vPOSITION, 1.0);
	vFragPosition = vPOSITION;

	vTexCoordOut = vTEXCOORD;
}
