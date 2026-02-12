#pragma once

#include "slang.h"


__declspec(dllexport) const char*
Attribute_getName(slang::Attribute** attribute)
{
    return (*attribute)->getName();
}

__declspec(dllexport) uint32_t
Attribute_getArgumentCount(slang::Attribute** attribute)
{
    return (*attribute)->getArgumentCount();
}

__declspec(dllexport) slang::TypeReflection*
Attribute_getArgumentType(slang::Attribute** attribute, uint32_t index)
{
    return (*attribute)->getArgumentType(index);
}

__declspec(dllexport) SlangResult
Attribute_getArgumentValueFloat(slang::Attribute** attribute, uint32_t index, float* value)
{
    return (*attribute)->getArgumentValueFloat(index, value);
}

__declspec(dllexport) SlangResult
Attribute_getArgumentValueInt(slang::Attribute** attribute, uint32_t index, int* value)
{
    return (*attribute)->getArgumentValueInt(index, value);
}


