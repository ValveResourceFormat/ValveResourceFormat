using System.Collections.Generic;
using ValveResourceFormat.Blocks;

namespace GUI.Types.Renderer
{
    public class GPUMeshBufferCache
    {
        private Dictionary<VBIB, GPUMeshBuffers> gpuBuffers = new Dictionary<VBIB, GPUMeshBuffers>();

        public GPUMeshBufferCache()
        {
        }

        public GPUMeshBuffers GetOrCreateVBIB(VBIB vbib)
        {
            if (gpuBuffers.TryGetValue(vbib, out var gpuVbib))
            {
                return gpuVbib;
            }
            else
            {
                var newGpuVbib = new GPUMeshBuffers(vbib);
                gpuBuffers.Add(vbib, newGpuVbib);
                return newGpuVbib;
            }
        }
    }
}
