#pragma once

#include "slang.h"

__declspec(dllexport) const char*
EntryPointReflection_getName(slang::EntryPointReflection** entryPoint)
{
    return (*entryPoint)->getName();
}

__declspec(dllexport) SlangStage
EntryPointReflection_getStage(slang::EntryPointReflection** entryPoint)
{
    return (*entryPoint)->getStage();
}

__declspec(dllexport) unsigned int
EntryPointReflection_getParameterCount(slang::EntryPointReflection** entryPoint)
{
    return (*entryPoint)->getParameterCount();
}

__declspec(dllexport) slang::VariableLayoutReflection*
EntryPointReflection_getVarLayout(slang::EntryPointReflection** entryPoint)
{
    return (*entryPoint)->getVarLayout();
}

__declspec(dllexport) slang::TypeLayoutReflection*
EntryPointReflection_getTypeLayout(slang::EntryPointReflection** entryPoint)
{
    return (*entryPoint)->getTypeLayout();
}

__declspec(dllexport) slang::VariableLayoutReflection*
EntryPointReflection_getResultVarLayout(slang::EntryPointReflection** entryPoint)
{
    return (*entryPoint)->getResultVarLayout();
}
