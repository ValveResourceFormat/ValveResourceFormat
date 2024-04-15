#version 460

#define F_EYEBALLS 0

struct EyePixelInput
{
    vec4 vEyeViewDir_PosX;
    vec4 vEyeRightDir_PosY;
    vec4 vEyeUpDir_PosZ;
};
