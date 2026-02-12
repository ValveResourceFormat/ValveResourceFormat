#pragma once
#include "slang.h"


__declspec(dllexport) SlangResult 
IGlobalSession_createSession(slang::IGlobalSession** globalSession, slang::SessionDesc const& desc, slang::ISession** outSession)
{
	return (*globalSession)->createSession(desc, outSession);
}

__declspec(dllexport) SlangProfileID 
IGlobalSession_findProfile(slang::IGlobalSession** globalSession, char const* name)
{
	return (*globalSession)->findProfile(name);
}