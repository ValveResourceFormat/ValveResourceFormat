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