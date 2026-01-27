using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern TypeLayoutReflectionPtr ShaderReflection_getGlobalParamsTypeLayout(ref ProgramLayoutPtr layout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern VariableLayoutReflectionPtr ShaderReflection_getGlobalParamsVarLayout(ref ProgramLayoutPtr layout);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern uint ShaderReflection_getParameterCount(ref ProgramLayoutPtr layout);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern ulong ShaderReflection_getEntryPointCount(ref ProgramLayoutPtr layout);


    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern EntryPointReflectionPtr ShaderReflection_getEntryPointByIndex(ref ProgramLayoutPtr layout, ulong index);

    public struct ProgramLayoutPtr
    {
        internal IntPtr ptr = 0;
        public ProgramLayoutPtr() { }
        public ProgramLayoutPtr(IntPtr p) { ptr = p; }

    }

    public class ProgramLayout
    {
        protected ProgramLayoutPtr Ptr;
        public ProgramLayout(ProgramLayoutPtr shaderReflectionPointer)
        {
            Ptr = shaderReflectionPointer;
        }
        public uint getParameterCount()
        {
            return ShaderReflection_getParameterCount(ref Ptr);
        }
        public TypeLayoutReflection getGlobalParamsTypeLayout()
        {
            return new TypeLayoutReflection(ShaderReflection_getGlobalParamsTypeLayout(ref Ptr));
        }

        public VariableLayoutReflection getGlobalParamsVarLayout()
        {
            return new VariableLayoutReflection(ShaderReflection_getGlobalParamsVarLayout(ref Ptr));
        }

        public ulong getEntryPointCount()
        {
            return ShaderReflection_getEntryPointCount(ref Ptr);
        }
        public EntryPointReflection getEntryPointByIndex(ulong index)
        {
            return new EntryPointReflection(ShaderReflection_getEntryPointByIndex(ref Ptr, index));
        }


    }
}
