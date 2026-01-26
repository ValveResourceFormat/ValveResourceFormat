using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Renderer.SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangTypeKind TypeReflection_getKind(ref TypeReflectionPtr typeReflection);


    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern uint TypeReflection_getFieldCount(ref TypeReflectionPtr typeReflection);


    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern uint TypeReflection_getRowCount(ref TypeReflectionPtr typeReflection);

    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern uint TypeReflection_getColumnCount(ref TypeReflectionPtr typeReflection);


    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResourceShape TypeReflection_getResourceShape(ref TypeReflectionPtr typeReflection);


    [DllImport("SlangSharpAPI", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr TypeReflection_getName(ref TypeReflectionPtr typeReflection);


    public struct TypeReflectionPtr
    {
        internal IntPtr ptr = 0;
        public TypeReflectionPtr() { }
        public TypeReflectionPtr(IntPtr p) { ptr = p; }
    }

    public class TypeReflection
    {
        protected TypeReflectionPtr Ptr;
        public TypeReflection(TypeReflectionPtr typeReflectionPointer)
        {
            Ptr = typeReflectionPointer;
        }

        public SlangTypeKind getKind()
        {
            return TypeReflection_getKind(ref Ptr);
        }
        public uint getFieldCount()
        {
            return TypeReflection_getFieldCount(ref Ptr);
        }
        public uint getRowCount()
        {
            return TypeReflection_getRowCount(ref Ptr);
        }
        public uint getColumnCount()
        {
            return TypeReflection_getColumnCount(ref Ptr);
        }

        public SlangResourceShape getResourceShape()
        {
            return TypeReflection_getResourceShape(ref Ptr);
        }

        public string getName()
        {
            return Marshal.PtrToStringUTF8(TypeReflection_getName(ref Ptr));
        }

    }
}
