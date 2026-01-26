using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Renderer.SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr EntryPointReflection_getName(ref EntryPointReflectionPtr entryPointReflection);

    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern uint EntryPointReflection_getParameterCount(ref EntryPointReflectionPtr entryPointReflection);

    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern TypeLayoutReflectionPtr EntryPointReflection_getTypeLayout(ref EntryPointReflectionPtr entryPointReflection);

    public struct EntryPointReflectionPtr
    {

        internal IntPtr ptr = 0;
        public EntryPointReflectionPtr() { }
        public EntryPointReflectionPtr(IntPtr p) { ptr = p; }
    }

    public struct EntryPointReflection
    {
        //this is how we talk to the C++ side
        EntryPointReflectionPtr ptr;
        public EntryPointReflection(EntryPointReflectionPtr entryPointReflectionPointer)
        {
            ptr = entryPointReflectionPointer;
        }

        public string getName()
        {
            return Marshal.PtrToStringUTF8(EntryPointReflection_getName(ref ptr));
        }

        public uint getParameterCount()
        {
            return EntryPointReflection_getParameterCount(ref ptr);
        }

        public TypeLayoutReflection getTypeLayout()
        {
            return new TypeLayoutReflection(EntryPointReflection_getTypeLayout(ref ptr));

        }
    }
}
