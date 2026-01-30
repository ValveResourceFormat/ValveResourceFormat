#pragma once

#include "slang.h"
#include <iostream>


__declspec(dllexport) SlangResult 
IComponentType_getTargetCode(slang::IComponentType** module, SlangInt targetIndex, slang::IBlob** outTargetCode)
{
	return (*module)->getTargetCode(targetIndex, outTargetCode);
}


__declspec(dllexport) SlangResult 
IComponentType_link(slang::IComponentType** componentType, slang::IComponentType** outLinkedComponentType, slang::IBlob** outDiagnostics)
{
	return (*componentType)->link(outLinkedComponentType, outDiagnostics);
}

__declspec(dllexport) SlangResult
IComponentType_linkWithOptions(slang::IComponentType** componentType, slang::IComponentType** outLinkedComponentType, uint32_t compilerOptionEntryCount, slang::CompilerOptionEntry* compilerOptionEntries, ISlangBlob** outDiagnostics)
{
    return (*componentType)->linkWithOptions(outLinkedComponentType, compilerOptionEntryCount, compilerOptionEntries, outDiagnostics);
}

__declspec(dllexport) slang::ProgramLayout*
IComponentType_getLayout(slang::IComponentType** componentType)
{
	return (*componentType)->getLayout();
}
