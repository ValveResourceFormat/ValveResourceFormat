#pragma once
#include "slang.h"

//Dangerous, because it assumes that createGlobalSession won't change in Slang. Perhaps there is a better way? We need this to export it.
//Then again, we could also just call slang_createGlobalSession2 directly from C#.

SlangResult createGlobalSession(slang::IGlobalSession** outGlobalSession)
{
	SlangGlobalSessionDesc defaultDesc = {};
	return slang_createGlobalSession2(&defaultDesc, outGlobalSession);
}

SlangResult createGlslCompatibleGlobalSession(slang::IGlobalSession** outGlobalSession)
{
	SlangGlobalSessionDesc defaultDesc = {};
	defaultDesc.enableGLSL = true;
	return slang_createGlobalSession2(&defaultDesc, outGlobalSession);
}


// HERE COME THE IGLOBALSESSION METHODS

__declspec(dllexport) SlangResult IGlobalSession_createSession(slang::IGlobalSession** globalSession, slang::SessionDesc const& desc, slang::ISession** outSession)
{
	return (*globalSession)->createSession(desc, outSession);
}

__declspec(dllexport) SlangProfileID IGlobalSession_findProfile(slang::IGlobalSession** globalSession, char const* name)
{
	return (*globalSession)->findProfile(name);
}