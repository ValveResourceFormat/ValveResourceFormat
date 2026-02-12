#pragma once

#include "slang.h"

__declspec(dllexport) unsigned int
VariableReflection_getUserAttributeCount(slang::VariableReflection** variableReflection)
{
    return (*variableReflection)->getUserAttributeCount();
}

__declspec(dllexport) slang::Attribute*
VariableReflection_getUserAttributeByIndex(slang::VariableReflection** variableReflection, unsigned int index)
{
    return (*variableReflection)->getUserAttributeByIndex(index);
}


//not supposed to use this one I think
__declspec(dllexport) slang::Attribute*
VariableReflection_findUserAttributeByName(slang::VariableReflection** variableReflection, SlangSession* globalSession, const char* name)
{
    return (*variableReflection)->findUserAttributeByName(globalSession, name);
}
