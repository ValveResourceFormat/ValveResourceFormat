using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal interface IMeshRenderer : IRenderer, IOctreeElement
    {
        IEnumerable<string> GetSupportedRenderModes();

        void SetRenderMode(string renderMode);
    }
}
