#pragma once

#include "slang.h"

__declspec(dllexport) slang::TypeLayoutReflection*
VariableLayoutReflection_getTypeLayout(slang::VariableLayoutReflection** variableLayout)
{
	return (*variableLayout)->getTypeLayout();
}

__declspec(dllexport) unsigned int
VariableLayoutReflection_getBindingIndex(slang::VariableLayoutReflection** variableLayout)
{
	return (*variableLayout)->getBindingIndex();
}

__declspec(dllexport) const char*
VariableLayoutReflection_getName(slang::VariableLayoutReflection** variableLayout)
{
	return (*variableLayout)->getName();
}

__declspec(dllexport) uint64_t
VariableLayoutReflection_getOffset(slang::VariableLayoutReflection** variableLayout, slang::ParameterCategory category)
{
	return (uint64_t)(*variableLayout)->getOffset(category);
}

__declspec(dllexport) slang::ParameterCategory
VariableLayoutReflection_getCategory(slang::VariableLayoutReflection** variableLayout)
{
	return (*variableLayout)->getCategory();
}
