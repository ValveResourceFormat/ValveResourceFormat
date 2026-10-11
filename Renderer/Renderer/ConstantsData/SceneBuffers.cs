using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.RHI;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Uniform buffer of handles to the buffers one scene shares across all its draws. Matches <c>SceneBuffers</c>
    /// in <c>common/SceneConstants.slang</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    internal struct SceneBuffers
    {
        /// <summary>Handle to the scene's <see cref="LightingConstants"/>.</summary>
        public DescriptorHandle<RHI.Buffer> Lighting;
    }
}
