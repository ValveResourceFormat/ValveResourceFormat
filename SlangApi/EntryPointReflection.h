#pragma once

#include "slang.h"

__declspec(dllexport) const char*
EntryPointReflection_getName(slang::EntryPointReflection** entryPoint)
{
	return (*entryPoint)->getName();
}

__declspec(dllexport) unsigned int
EntryPointReflection_getParameterCount(slang::EntryPointReflection** entryPoint)
{
	return (*entryPoint)->getParameterCount();
}

__declspec(dllexport) slang::TypeLayoutReflection*
EntryPointReflection_getTypeLayout(slang::EntryPointReflection** entryPoint)
{
	return (*entryPoint)->getTypeLayout();
}