using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal interface IRenderer
    {
        void Render(Camera camera);

        void Update(float frameTime);
    }
}
