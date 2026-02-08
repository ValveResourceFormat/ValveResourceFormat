using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr ISlangBlob_getBufferPointer(ref ISlangBlobPtr blobPtr);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr ISlangBlob_getBufferSize(ref ISlangBlobPtr blobPtr);


    public struct ISlangBlobPtr
    {
        internal IntPtr ptr = 0;
        public ISlangBlobPtr() { }
        public ISlangBlobPtr(IntPtr p) { ptr = p; }
    }

    public struct ISlangBlob
    {
        //this is how we talk to the C++ side
        ISlangBlobPtr ptr;
        public ISlangBlob(ISlangBlobPtr slangBlobPointer)
        {
            ptr = slangBlobPointer;
        }

        public bool isNull()
        {
            return ptr.ptr == IntPtr.Zero;
        }

        public IntPtr getBufferPointer()
        {
            return ISlangBlob_getBufferPointer(ref ptr);
        }

        public long getBufferSize()
        {
            return ISlangBlob_getBufferSize(ref ptr);
        }

        public string getString()
        {
            if (isNull())
            {
                return "";
            }
            return Marshal.PtrToStringUTF8(getBufferPointer(), (int)getBufferSize());
        }
    }
}

