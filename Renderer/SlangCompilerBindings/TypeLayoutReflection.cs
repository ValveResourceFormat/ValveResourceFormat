using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;


namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangTypeKind TypeLayoutReflection_getKind(ref TypeLayoutReflectionPtr typeLayout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern TypeReflectionPtr TypeLayoutReflection_getType(ref TypeLayoutReflectionPtr typeLayout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern uint TypeLayoutReflection_getFieldCount(ref TypeLayoutReflectionPtr typeLayout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern VariableLayoutReflectionPtr TypeLayoutReflection_getFieldByIndex(ref TypeLayoutReflectionPtr typeLayout, uint index);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern TypeLayoutReflectionPtr TypeLayoutReflection_getElementTypeLayout(ref TypeLayoutReflectionPtr typeLayout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern VariableLayoutReflectionPtr TypeLayoutReflection_getElementVarLayout(ref TypeLayoutReflectionPtr typeLayout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern ulong TypeLayoutReflection_getSize(ref TypeLayoutReflectionPtr typeLayout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern ulong TypeLayoutReflection_getStride(ref TypeLayoutReflectionPtr typeLayout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern ParameterCategory TypeLayoutReflection_getParameterCategory(ref TypeLayoutReflectionPtr typeLayout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern VariableLayoutReflectionPtr TypeLayoutReflection_getContainerVarLayout(ref TypeLayoutReflectionPtr typeLayout);

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

        public SlangTypeKind getKind()
        {
            return TypeLayoutReflection_getKind(ref Ptr);
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

        public TypeLayoutReflection getElementTypeLayout()
        {
            return new TypeLayoutReflection(TypeLayoutReflection_getElementTypeLayout(ref Ptr));
        }
        public VariableLayoutReflection getElementVarLayout()
        {
            return new VariableLayoutReflection(TypeLayoutReflection_getElementVarLayout(ref Ptr));
        }

        public ulong getSize()
        {
            return TypeLayoutReflection_getSize(ref Ptr);
        }

        public ulong getStride()
        {
            return TypeLayoutReflection_getStride(ref Ptr);
        }

        public ParameterCategory getParameterCategory()
        {
            return TypeLayoutReflection_getParameterCategory(ref Ptr);
        }

        public VariableLayoutReflection getContainerVarLayout()
        {
            return new VariableLayoutReflection(TypeLayoutReflection_getContainerVarLayout(ref Ptr));
        }
    }
}
