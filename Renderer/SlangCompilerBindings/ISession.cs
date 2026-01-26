using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Renderer.SlangCompiler;

public partial class SlangBindings
{
    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern IModulePtr ISession_loadModule(ref ISessionPtr session, [MarshalAs(UnmanagedType.LPStr)] string path, out ISlangBlob diagnosticBlob);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult ISession_createCompositeComponentType(ref ISessionPtr session, IComponentTypePtr[] components, int componentCount, out IComponentTypePtr outComponentType, out ISlangBlob diagnostics);


    public struct ISessionPtr
    {
        internal IntPtr ptr = 0;
        public ISessionPtr() { }
        public ISessionPtr(IntPtr p) { ptr = p; }
    }

    public class ISession
    {
        ISessionPtr Ptr;
        public ISession(ISessionPtr sessionPointer)
        {
            Ptr = sessionPointer;
        }
    }
}

