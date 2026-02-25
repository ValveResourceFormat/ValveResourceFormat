#version 460

#ifndef VRF_CSGO_CHARACTER_EYES_COMMON_INCLUDE
#define VRF_CSGO_CHARACTER_EYES_COMMON_INCLUDE

#define F_EYEBALLS 0 // csgo_character
#ifndef D_ANIMATED
#define D_ANIMATED 0
#endif

struct EyePixelInput
{
    vec4 vEyeViewDir_PosX;
    vec4 vEyeRightDir_PosY;
    vec4 vEyeUpDir_PosZ;
};

#endif
