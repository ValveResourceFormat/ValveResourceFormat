using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr EntryPointReflection_getName(ref EntryPointReflectionPtr entryPointReflection);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangStage EntryPointReflection_getStage(ref EntryPointReflectionPtr entryPointReflection);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern uint EntryPointReflection_getParameterCount(ref EntryPointReflectionPtr entryPointReflection);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern VariableLayoutReflectionPtr EntryPointReflection_getVarLayout(ref EntryPointReflectionPtr entryPointReflection);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern TypeLayoutReflectionPtr EntryPointReflection_getTypeLayout(ref EntryPointReflectionPtr entryPointReflection);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern VariableLayoutReflectionPtr EntryPointReflection_getResultVarLayout(ref EntryPointReflectionPtr entryPointReflection);


    public struct EntryPointReflectionPtr
    {

        internal IntPtr ptr = 0;
        public EntryPointReflectionPtr() { }
        public EntryPointReflectionPtr(IntPtr p) { ptr = p; }
    }

    public struct EntryPointReflection
    {
        //this is how we talk to the C++ side
        EntryPointReflectionPtr Ptr;
        public EntryPointReflection(EntryPointReflectionPtr entryPointReflectionPointer)
        {
            Ptr = entryPointReflectionPointer;
        }

        public string getName()
        {
            return Marshal.PtrToStringUTF8(EntryPointReflection_getName(ref Ptr));
        }

        public SlangStage getStage()
        {
            return EntryPointReflection_getStage(ref Ptr);
        }

        public uint getParameterCount()
        {
            return EntryPointReflection_getParameterCount(ref Ptr);
        }

        public VariableLayoutReflection getVarLayout()
        {
            return new VariableLayoutReflection(EntryPointReflection_getVarLayout(ref Ptr));
        }

        public TypeLayoutReflection getTypeLayout()
        {
            return new TypeLayoutReflection(EntryPointReflection_getTypeLayout(ref Ptr));
        }

        public VariableLayoutReflection getResultVarLayout()
        {
            return new VariableLayoutReflection(EntryPointReflection_getResultVarLayout(ref Ptr));
        }
    }
}
