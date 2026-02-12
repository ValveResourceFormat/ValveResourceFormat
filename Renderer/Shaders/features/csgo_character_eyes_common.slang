#version 460

public extern static const int F_EYEBALLS = 0;

public struct EyePixelInput
{
    public vec4 vEyeViewDir_PosX;
    public vec4 vEyeRightDir_PosY;
    public vec4 vEyeUpDir_PosZ;
};
