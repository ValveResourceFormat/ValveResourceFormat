using System;
using System.Collections.Generic;
using System.Diagnostics;
using GUI.Types.Renderer;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace GUI.Utils
{
    class VrfGuiContext : IDisposable
    {
        public string FileName { get; }

        public Package CurrentPackage { get; set; }

        public MaterialLoader MaterialLoader { get; }

        public ShaderLoader ShaderLoader { get; }
        public GPUMeshBufferCache MeshBufferCache { get; }
        public AdvancedGuiFileLoader FileLoader { get; }
        public VrfGuiContext ParentGuiContext { get; private set; }

        // TODO: This buffer should not be here
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public QuadIndexBuffer QuadIndices
        {
            get
            {
                quadIndices ??= new QuadIndexBuffer(65532);

                return quadIndices;
            }
        }

        private QuadIndexBuffer quadIndices;

        public VrfGuiContext(string fileName, VrfGuiContext parentGuiContext)
        {
            FileName = fileName;
            MaterialLoader = new MaterialLoader(this);
            ShaderLoader = new ShaderLoader();
            FileLoader = new AdvancedGuiFileLoader(this);
            MeshBufferCache = new GPUMeshBufferCache();
            ParentGuiContext = parentGuiContext;
            CurrentPackage = parentGuiContext?.CurrentPackage;
        }

        public Resource LoadFileByAnyMeansNecessary(string file) =>
            FileLoader.LoadFile(file);

        public void ClearCache()
        {
            FileLoader.ClearCache();
            //ShaderLoader.ClearCache();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ParentGuiContext = null;

                if (CurrentPackage != null)
                {
                    CurrentPackage.Dispose();
                    CurrentPackage = null;
                }

                FileLoader.Dispose();
                ShaderLoader.Dispose();
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
