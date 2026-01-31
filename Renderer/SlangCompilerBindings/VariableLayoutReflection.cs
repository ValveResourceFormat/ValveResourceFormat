using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern TypeLayoutReflectionPtr VariableLayoutReflection_getTypeLayout(ref VariableLayoutReflectionPtr variableLayout);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr VariableLayoutReflection_getName(ref VariableLayoutReflectionPtr variableLayout);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern ulong VariableLayoutReflection_getOffset(ref VariableLayoutReflectionPtr variableLayout, ParameterCategory category);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern uint VariableLayoutReflection_getBindingIndex(ref VariableLayoutReflectionPtr variableLayout);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern ParameterCategory VariableLayoutReflection_getCategory(ref VariableLayoutReflectionPtr variableLayout);

    public struct VariableLayoutReflectionPtr
    {
        internal IntPtr ptr = 0;
        public VariableLayoutReflectionPtr() { }
        public VariableLayoutReflectionPtr(IntPtr p) { ptr = p; }

    }

    public class VariableLayoutReflection
    {
        protected VariableLayoutReflectionPtr Ptr;

        public VariableLayoutReflection(VariableLayoutReflectionPtr variableLayoutReflectionPointer)
        {
            Ptr = variableLayoutReflectionPointer;
        }

        public TypeLayoutReflection getTypeLayout()
        {
            return new TypeLayoutReflection(VariableLayoutReflection_getTypeLayout(ref Ptr));
        }

        public string getName()
        {
            return Marshal.PtrToStringUTF8(VariableLayoutReflection_getName(ref Ptr));
        }

        public ulong getOffset(ParameterCategory category = ParameterCategory.eUniform)
        {
            return VariableLayoutReflection_getOffset(ref Ptr, category);
        }

        public uint getBindingIndex()
        {
            return VariableLayoutReflection_getBindingIndex(ref Ptr);
        }

        public ParameterCategory getCategory()
        {
            return VariableLayoutReflection_getCategory(ref Ptr);
        }
    }
}
