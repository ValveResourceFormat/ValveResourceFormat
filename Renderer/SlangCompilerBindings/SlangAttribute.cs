using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Attribute_getName(ref SlangAttributePtr attribute);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Attribute_getArgumentCount(ref SlangAttributePtr attribute);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    public static extern TypeReflectionPtr Attribute_getArgumentType(ref SlangAttributePtr attribute, uint index);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    public static extern SlangResult Attribute_getArgumentValueFloat(ref SlangAttributePtr attribute, uint index, out float value);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    public static extern SlangResult Attribute_getArgumentValueInt(ref SlangAttributePtr attribute, uint index, out int value);



    public struct SlangAttributePtr
    {

        internal IntPtr ptr = 0;
        public SlangAttributePtr() { }
        public SlangAttributePtr(IntPtr p) { ptr = p; }
    }
    //Slang prefix because of naming conflicts
    public struct SlangAttribute
    {
        SlangAttributePtr Ptr;

        public SlangAttribute(SlangAttributePtr p) { Ptr = p; }

        public string getName()
        {
            return Marshal.PtrToStringUTF8(Attribute_getName(ref Ptr));
        }

        public uint getArgumentCount()
        {
            return Attribute_getArgumentCount(ref Ptr);
        }

        public TypeReflection getArgumentType(uint index)
        {
            return new TypeReflection(Attribute_getArgumentType(ref Ptr, index));
        }

        public SlangResult getArgumentValueFloat(uint index, out float output)
        {
            return Attribute_getArgumentValueFloat(ref Ptr, index, out output);
        }

        public SlangResult getArgumentValueInt(uint index, out int output)
        {
            return Attribute_getArgumentValueInt(ref Ptr, index, out output);
        }
    }

}
