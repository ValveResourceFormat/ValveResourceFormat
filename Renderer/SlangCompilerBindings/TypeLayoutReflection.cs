using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;


namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern TypeReflectionPtr TypeLayoutReflection_getType(ref TypeLayoutReflectionPtr typeLayout);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern uint TypeLayoutReflection_getFieldCount(ref TypeLayoutReflectionPtr typeLayout);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern VariableLayoutReflectionPtr TypeLayoutReflection_getFieldByIndex(ref TypeLayoutReflectionPtr typeLayout, uint index);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern ParameterCategory TypeLayoutReflection_getParameterCategory(ref TypeLayoutReflectionPtr typeLayout);

    public struct TypeLayoutReflectionPtr
    {
        internal IntPtr ptr = 0;
        public TypeLayoutReflectionPtr() { }
        public TypeLayoutReflectionPtr(IntPtr p) { ptr = p; }
    }

    public class TypeLayoutReflection
    {
        protected TypeLayoutReflectionPtr Ptr;
        public TypeLayoutReflection(TypeLayoutReflectionPtr typeLayoutReflectionPointer)
        {
            Ptr = typeLayoutReflectionPointer;
        }

        public TypeReflection getType()
        {
            return new TypeReflection(TypeLayoutReflection_getType(ref Ptr));
        }
        public uint getFieldCount()
        {
            return TypeLayoutReflection_getFieldCount(ref Ptr);
        }

        public VariableLayoutReflection getFieldByIndex(uint index)
        {
            return new VariableLayoutReflection(TypeLayoutReflection_getFieldByIndex(ref Ptr, index));
        }

        public ParameterCategory getParameterCategory()
        {
            return TypeLayoutReflection_getParameterCategory(ref Ptr);
        }
    }
}
