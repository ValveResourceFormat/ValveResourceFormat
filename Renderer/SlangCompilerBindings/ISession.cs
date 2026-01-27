using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern IModulePtr ISession_loadModule(ref ISessionPtr session, [MarshalAs(UnmanagedType.LPStr)] string path, out ISlangBlob diagnosticBlob);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult ISession_createCompositeComponentType(ref ISessionPtr session, IComponentTypePtr[] components, int componentCount, out IComponentTypePtr outComponentType, out ISlangBlob diagnostics);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern uint ISession_release(ref ISessionPtr session);


    public struct ISessionPtr
    {
        internal IntPtr ptr = 0;
        public ISessionPtr() { }
        public ISessionPtr(IntPtr p) { ptr = p; }
    }

    public class ISession
    {
        //this is how we talk to the C++ side
        ISessionPtr Ptr;
        public ISession(ISessionPtr sessionPointer)
        {
            Ptr = sessionPointer;
        }

        public IModule loadModule(string path, out ISlangBlob diagnosticBlob)
        {
            return new IModule(ISession_loadModule(ref Ptr, path, out diagnosticBlob));
        }

        public SlangResult createCompositeComponentType(IComponentType[] components, out IComponentType outComponentType, out ISlangBlob diagnostics)
        {
            IComponentTypePtr[] componentPtrs = new IComponentTypePtr[components.Length];
            for (int i = 0; i < components.Length; i++)
            {
                componentPtrs[i] = components[i].Ptr;
            }
            SlangResult result = ISession_createCompositeComponentType(ref Ptr, componentPtrs, componentPtrs.Length, out IComponentTypePtr outComponentTypePtr, out diagnostics);
            outComponentType = new IComponentType(outComponentTypePtr);
            return result;
        }

        public bool isNull()
        {
            return Ptr.ptr == IntPtr.Zero;
        }

        public uint release()
        {
            if(!isNull())
                return ISession_release(ref Ptr);
            return 0;
        }
    }
}

