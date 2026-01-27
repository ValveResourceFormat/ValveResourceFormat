using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SlangCompiler;


public partial class SlangBindings
{
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult slang_createGlobalSession2(ref SlangGlobalSessionDesc desc, out IGlobalSessionPtr outGlobalSession);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangProfileID IGlobalSession_findProfile(ref IGlobalSessionPtr globalSession, [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("SlangApi", CallingConvention = CallingConvention.Cdecl)]
    static extern SlangResult IGlobalSession_createSession(ref IGlobalSessionPtr globalSession, ref SessionDesc desc, out ISessionPtr outSession);

    public static SlangResult createGlobalSession(out IGlobalSession outGlobalSession)
    {
        SlangGlobalSessionDesc desc = new SlangGlobalSessionDesc();
        SlangResult result = slang_createGlobalSession2(ref desc, out IGlobalSessionPtr sessionPointer);


        outGlobalSession = new IGlobalSession(sessionPointer);
        return result;
    }
    public static SlangResult createGlslCompatibleGlobalSession(out IGlobalSession outGlobalSession)
    {
        SlangGlobalSessionDesc desc = new SlangGlobalSessionDesc();
        desc.enableGLSL = true;
        SlangResult result = slang_createGlobalSession2(ref desc, out IGlobalSessionPtr sessionPointer);


        outGlobalSession = new IGlobalSession(sessionPointer);
        return result;
    }

    public struct IGlobalSessionPtr
    {
        internal IntPtr ptr = 0;

        public IGlobalSessionPtr() { }
        public IGlobalSessionPtr(IntPtr p) { ptr = p; }
    }

    public class IGlobalSession
    {
        //this is how we talk to the C++ side
        IGlobalSessionPtr Ptr;

        public IGlobalSession(IGlobalSessionPtr globalSessionPointer)
        {
            Ptr = globalSessionPointer;
        }

        public SlangResult createSession(SessionDesc desc, out ISession outSession)
        {
            // Call the native function to create a session
            SlangResult result = IGlobalSession_createSession(ref Ptr, ref desc, out ISessionPtr outSessionPtr);

            outSession = new ISession(outSessionPtr);
            return result;
        }

        public SlangProfileID findProfile(string name)
        {
            return IGlobalSession_findProfile(ref Ptr, name);
        }

        public bool isNull()
        {
            return Ptr.ptr == IntPtr.Zero;
        }
    }
}

