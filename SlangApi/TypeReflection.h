#pragma once

#include "slang.h"




__declspec(dllexport) slang::TypeReflection::Kind
TypeReflection_getKind(slang::TypeReflection** typeReflection)
{
	return (*typeReflection)->getKind();
}

__declspec(dllexport) unsigned int
TypeReflection_getFieldCount(slang::TypeReflection** typeReflection)
{
	return (*typeReflection)->getFieldCount();
}


//HACK: For some reason, (*typeReflection)->getElementCount() doesn't work. Importing its sub-function sp_ReflectionType_GetSpecializedElementCount fails. God knows why
__declspec(dllexport) uint64_t
TypeReflection_getElementCount(slang::TypeReflection** typeReflection)
{
    return spReflectionType_GetElementCount((SlangReflectionType*)*typeReflection);
}

__declspec(dllexport) unsigned int
TypeReflection_getRowCount(slang::TypeReflection** typeReflection)
{
	return (*typeReflection)->getRowCount();
}

__declspec(dllexport) unsigned int
TypeReflection_getColumnCount(slang::TypeReflection** typeReflection)
{
	return (*typeReflection)->getColumnCount();
}

__declspec(dllexport) slang::TypeReflection::ScalarType
TypeReflection_getScalarType(slang::TypeReflection** typeReflection)
{
    return (*typeReflection)->getScalarType();
}

__declspec(dllexport) SlangResourceShape
TypeReflection_getResourceShape(slang::TypeReflection** typeReflection)
{
	return (*typeReflection)->getResourceShape();
}

__declspec(dllexport) SlangResourceAccess
TypeReflection_getResourceAccess(slang::TypeReflection** typeReflection)
{
	return (*typeReflection)->getResourceAccess();
}

__declspec(dllexport) const char*
TypeReflection_getName(slang::TypeReflection** typeReflection)
{
	return (*typeReflection)->getName();
}
