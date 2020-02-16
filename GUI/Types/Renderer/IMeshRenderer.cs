using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal interface IMeshRenderer
    {
        void Render(Camera camera);

        void Update(float frameTime);

        IEnumerable<string> GetSupportedRenderModes();

        void SetRenderMode(string renderMode);
    }
}
