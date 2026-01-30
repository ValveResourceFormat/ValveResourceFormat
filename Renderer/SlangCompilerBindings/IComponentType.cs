using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult IComponentType_getTargetCode(ref IComponentTypePtr component, long target, out ISlangBlob outBlob);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult IComponentType_link(ref IComponentTypePtr component, out IComponentTypePtr outLinked, out ISlangBlob diagnostics);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult IComponentType_linkWithOptions(ref IComponentTypePtr component, out IComponentTypePtr outLinked, uint compilerOptionsEntryCount, CompilerOptionEntry[] optionsEntries, out ISlangBlob diagnostics);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern ProgramLayoutPtr IComponentType_getLayout(ref IComponentTypePtr component);

    public struct IComponentTypePtr
    {
        internal IntPtr ptr = 0;
        public IComponentTypePtr() { }
        public IComponentTypePtr(IntPtr p) { ptr = p; }
    }

    public class IComponentType
    {
        //this is dangerous wee woo wee woo. Necessary evil for now (see createCompositeComponentType)
        public IComponentTypePtr Ptr;


        public IComponentType(IComponentTypePtr componentTypePointer)
        {
            Ptr = componentTypePointer;
        }

        public bool isNull()
        {
            return Ptr.ptr == IntPtr.Zero;
        }
        public SlangResult getTargetCode(long target, out ISlangBlob outBlob)
        {
            SlangResult result = IComponentType_getTargetCode(ref Ptr, target, out outBlob);
            return result;
        }

        public SlangResult link(out IComponentType returnedComponent, out ISlangBlob diagnostics)
        {
            SlangResult ret = IComponentType_link(ref Ptr, out IComponentTypePtr outComponent, out diagnostics);
            returnedComponent = new IComponentType(outComponent);
            return ret;
        }

        public SlangResult linkWithOptions(out IComponentType returnedComponent, CompilerOptionEntry[] compilerOptions, out ISlangBlob diagnostics)
        {
            SlangResult ret = IComponentType_linkWithOptions(ref Ptr, out IComponentTypePtr outComponent, (uint)compilerOptions.Length, compilerOptions, out diagnostics);
            returnedComponent = new IComponentType(outComponent);
            return ret;
        }
        public ProgramLayout getLayout()
        {
            return new ProgramLayout(IComponentType_getLayout(ref Ptr));
        }
    }
}
