using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal interface IMeshRenderer : IRenderer
    {
        string LayerName { get; }

        IEnumerable<string> GetSupportedRenderModes();

        void SetRenderMode(string renderMode);
    }
}
