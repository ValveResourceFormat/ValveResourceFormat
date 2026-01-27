#pragma once


#include "slang.h"

//This is typedeffed as ProgramLayout in slang

__declspec(dllexport) unsigned int
ShaderReflection_getParameterCount(slang::ShaderReflection** shaderReflection)
{
	return (*shaderReflection)->getParameterCount();
}

__declspec(dllexport) slang::TypeLayoutReflection*
ShaderReflection_getGlobalParamsTypeLayout(slang::ShaderReflection** shaderReflection)
{
	return (*shaderReflection)->getGlobalParamsTypeLayout();
}

__declspec(dllexport) slang::VariableLayoutReflection*
ShaderReflection_getGlobalParamsVarLayout(slang::ShaderReflection** shaderReflection)
{
	return (*shaderReflection)->getGlobalParamsVarLayout();
}

__declspec(dllexport) uint64_t
ShaderReflection_getEntryPointCount(slang::ShaderReflection** shaderReflection)
{
	return (*shaderReflection)->getEntryPointCount();
}

__declspec(dllexport) slang::EntryPointReflection*
ShaderReflection_getEntryPointByIndex(slang::ShaderReflection** shaderReflection, SlangUInt index)
{
	return (*shaderReflection)->getEntryPointByIndex(index);
}
