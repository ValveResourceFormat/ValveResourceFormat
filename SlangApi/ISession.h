#pragma once

#include "slang.h"

__declspec(dllexport) slang::IModule*
ISession_loadModule(slang::ISession** session, const char* name, slang::IBlob** diagnosticBlob)
{
	return (*session)->loadModule(name, diagnosticBlob);
}

__declspec(dllexport) slang::IModule*
ISession_loadModuleFromSourceString(slang::ISession** session, const char* name, const char* path, const char* source, slang::IBlob** diagnosticBlob)
{
    return (*session)->loadModuleFromSourceString(name, path, source, diagnosticBlob);
}


__declspec(dllexport) SlangResult
ISession_createCompositeComponentType(
	slang::ISession** session,
	slang::IComponentType** componentTypes,
	SlangInt componentTypeCount,
	slang::IComponentType** outCompositeComponentType,
	slang::IBlob** outDiagnostics)
{
	return (*session)->createCompositeComponentType(
		componentTypes,
		componentTypeCount,
		outCompositeComponentType,
		outDiagnostics);
}

__declspec(dllexport) unsigned int
ISession_release(slang::ISession** session)
{
    return (*session)->release();
}
