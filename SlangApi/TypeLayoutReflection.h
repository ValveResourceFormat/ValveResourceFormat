#pragma once

#include "slang.h"

__declspec(dllexport) slang::TypeReflection*
TypeLayoutReflection_getType(slang::TypeLayoutReflection** typeReflection)
{
	return (*typeReflection)->getType();
}


__declspec(dllexport) unsigned int
TypeLayoutReflection_getFieldCount (slang::TypeLayoutReflection** typeLayout)
{
	return (*typeLayout)->getFieldCount();
}


__declspec(dllexport) slang::VariableLayoutReflection*
TypeLayoutReflection_getFieldByIndex (slang::TypeLayoutReflection** typeLayout, unsigned int index)
{
	return (*typeLayout)->getFieldByIndex(index);
}

__declspec(dllexport) slang::ParameterCategory
TypeLayoutReflection_getParameterCategory(slang::TypeLayoutReflection** typeLayout)
{
	return (*typeLayout)->getParameterCategory();
}
