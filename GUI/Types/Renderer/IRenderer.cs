using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal interface IRenderer
    {
        AABB BoundingBox { get; }

        string LayerName { get; }

        void Render(Camera camera, RenderPass renderPass);

        void Update(float frameTime);
    }
}
