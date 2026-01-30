#pragma once

#include "slang.h"

__declspec(dllexport) slang::TypeReflection::Kind
TypeLayoutReflection_getKind(slang::TypeLayoutReflection** typeReflection)
{
    return (*typeReflection)->getKind();
}


__declspec(dllexport) slang::TypeReflection*
TypeLayoutReflection_getType(slang::TypeLayoutReflection** typeReflection)
{
    return (*typeReflection)->getType();
}


__declspec(dllexport) unsigned int
TypeLayoutReflection_getFieldCount(slang::TypeLayoutReflection** typeLayout)
{
    return (*typeLayout)->getFieldCount();
}

__declspec(dllexport) slang::VariableLayoutReflection*
TypeLayoutReflection_getFieldByIndex(slang::TypeLayoutReflection** typeLayout, unsigned int index)
{
    return (*typeLayout)->getFieldByIndex(index);
}

__declspec(dllexport) slang::TypeLayoutReflection*
TypeLayoutReflection_getElementTypeLayout(slang::TypeLayoutReflection** typeLayout)
{
    return (*typeLayout)->getElementTypeLayout();
}

__declspec(dllexport) slang::VariableLayoutReflection*
TypeLayoutReflection_getElementVarLayout(slang::TypeLayoutReflection** typeLayout)
{
    return (*typeLayout)->getElementVarLayout();
}

__declspec(dllexport) uint64_t
TypeLayoutReflection_getSize(slang::TypeLayoutReflection** typeLayout)
{
    return (*typeLayout)->getSize();
}

__declspec(dllexport) uint64_t
TypeLayoutReflection_getStride(slang::TypeLayoutReflection** typeLayout)
{
    return (*typeLayout)->getStride();
}


__declspec(dllexport) slang::ParameterCategory
TypeLayoutReflection_getParameterCategory(slang::TypeLayoutReflection** typeLayout)
{
    return (*typeLayout)->getParameterCategory();
}


__declspec(dllexport) slang::VariableLayoutReflection*
TypeLayoutReflection_getContainerVarLayout(slang::TypeLayoutReflection** typeLayout)
{
    return (*typeLayout)->getContainerVarLayout();
}
