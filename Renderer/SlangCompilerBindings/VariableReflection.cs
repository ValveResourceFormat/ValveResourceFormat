using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static SlangCompiler.SlangBindings;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint VariableReflection_getUserAttributeCount(ref VariableReflectionPtr variableReflection);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    public static extern SlangAttributePtr VariableReflection_getUserAttributeByIndex(ref VariableReflectionPtr variableReflection, uint index);

    //I think this is used to list the globally known arguments. We do not need it for now
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    public static extern SlangAttributePtr VariableReflection_findUserAttributeByName(ref VariableReflectionPtr variableReflection, IGlobalSessionPtr globalSession, [MarshalAs(UnmanagedType.LPStr)] string name);

    public struct VariableReflectionPtr
    {
        internal IntPtr ptr = 0;
        public VariableReflectionPtr() { }
        public VariableReflectionPtr(IntPtr p) { ptr = p; }
    }

    public struct VariableReflection
    {
        VariableReflectionPtr Ptr;

        public VariableReflection(VariableReflectionPtr p) { Ptr = p; }

        public uint getUserAttributeCount()
        {
            return VariableReflection_getUserAttributeCount(ref Ptr);
        }
        public SlangAttribute getUserAttributeByIndex(uint index)
        {
            return new SlangAttribute(VariableReflection_getUserAttributeByIndex(ref Ptr, index));
        }

    }
}
