using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult IModule_findEntryPointByName(ref IModulePtr module, [MarshalAs(UnmanagedType.LPStr)] string name, out IEntryPointPtr outEntryPoint);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern int IModule_getDefinedEntryPointCount(ref IModulePtr module);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult IModule_getDefinedEntryPoint(ref IModulePtr module, int index, out IEntryPointPtr outEntryPoint);

    public struct IModulePtr
    {
        internal IntPtr ptr = 0;
        public IModulePtr() { }
        public IModulePtr(IntPtr p) { ptr = p; }
    }

    public class IModule : IComponentType
    {
        public IModule(IModulePtr modulePointer) : base(new IComponentTypePtr(modulePointer.ptr)) { }

        public IModulePtr GetPointer()
        {
            return new IModulePtr(Ptr.ptr);
        }

        //CAREFUL: You can not get target code for the returned entry point, you must first use IComponentType.link!
        public SlangResult findEntryPointByName(string name, out IEntryPoint outEntryPoint)
        {
            IModulePtr selfPointer = GetPointer();

            SlangResult result = IModule_findEntryPointByName(ref selfPointer, name, out IEntryPointPtr entryPointPtr);
            outEntryPoint = new IEntryPoint(entryPointPtr);
            return result;
        }

        public int getDefinedEntryPointCount()
        {
            IModulePtr selfPointer = GetPointer();
            return IModule_getDefinedEntryPointCount(ref selfPointer);
        }

        //CAREFUL: You can not get target code for the returned entry point, you must first use IComponentType.link!
        public SlangResult getDefinedEntryPoint(int index, out IEntryPoint outEntryPoint)
        {
            IModulePtr selfPointer = GetPointer();
            SlangResult result = IModule_getDefinedEntryPoint(ref selfPointer, index, out IEntryPointPtr entryPointPtr);
            outEntryPoint = new IEntryPoint(entryPointPtr);
            return result;
        }
    }
}
