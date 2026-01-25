#pragma once

#include "slang.h"


__declspec(dllexport) const void* 
ISlangBlob_getBufferPointer(slang::IBlob** blob)
{
	return (*blob)->getBufferPointer();
}

__declspec(dllexport) uint64_t 
ISlangBlob_getBufferSize(slang::IBlob** blob)
{
	return (*blob)->getBufferSize();
}