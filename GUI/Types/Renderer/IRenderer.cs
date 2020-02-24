using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal interface IRenderer
    {
        void Render(Camera camera, RenderPass renderPass);

        void Update(float frameTime);
    }
}
