using System;
using System.Collections.Generic;
using System.Text;

namespace SlangCompiler;

public partial class SlangBindings
{
    public struct IEntryPointPtr
    {
        internal IntPtr ptr = 0;
        public IEntryPointPtr() { }
        public IEntryPointPtr(IntPtr p) { ptr = p; }
    }

    public class IEntryPoint : IComponentType
    {
        public IEntryPoint(IEntryPointPtr entryPointPointer) : base(new IComponentTypePtr(entryPointPointer.ptr)) { }
    }
}
