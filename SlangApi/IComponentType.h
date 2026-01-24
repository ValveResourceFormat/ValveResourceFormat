#pragma once

#include "slang.h"
#include <iostream>


__declspec(dllexport) SlangResult IComponentType_getTargetCode(slang::IComponentType** module, SlangInt targetIndex, slang::IBlob** outTargetCode)
{
	std::cout << "to be consumed pointer: " << std::hex << *module << std::endl;
	return (*module)->getTargetCode(targetIndex, outTargetCode);
}


__declspec(dllexport) SlangResult IComponentType_link(slang::IComponentType** componentType, slang::IComponentType** outLinkedComponentType, slang::IBlob** outDiagnostics)
{
	return (*componentType)->link(outLinkedComponentType, outDiagnostics);
}