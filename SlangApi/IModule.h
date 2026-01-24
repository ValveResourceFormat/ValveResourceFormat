#pragma once

#include "slang.h"
#include <iostream>



//DO NOT USE, IT IS BROKEN
__declspec(dllexport) SlangResult 
IModule_findEntryPointByName(slang::IModule** module, char const* name, slang::IEntryPoint** outEntryPoint)
{
	auto ret =  (*module)->findEntryPointByName(name, outEntryPoint);
	std::cout << std::hex << "returned entry pointer: " << *outEntryPoint << std::endl;
	return ret;
}

__declspec(dllexport) SlangInt32 
IModule_getDefinedEntryPointCount(slang::IModule** module)
{
	return (*module)->getDefinedEntryPointCount();
}

__declspec(dllexport) SlangResult 
IModule_getDefinedEntryPoint(slang::IModule** module, SlangInt32 index, slang::IEntryPoint** outEntryPoint)
{
	return (*module)->getDefinedEntryPoint(index, outEntryPoint);
}

