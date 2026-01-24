#pragma once

#include "slang.h"

__declspec(dllexport) slang::IModule* 
ISession_loadModule(slang::ISession** session, const char* name, slang::IBlob** diagnosticBlob)
{
	return (*session)->loadModule(name, diagnosticBlob);
}