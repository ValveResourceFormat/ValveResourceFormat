using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal interface IMeshRenderer : IRenderer
    {
        IEnumerable<string> GetSupportedRenderModes();

        void SetRenderMode(string renderMode);
    }
}
