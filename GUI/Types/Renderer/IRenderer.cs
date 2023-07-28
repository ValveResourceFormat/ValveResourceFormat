namespace GUI.Types.Renderer
{
    internal interface IRenderer
    {
        AABB LocalBoundingBox { get; }

        void Render(Camera camera, RenderPass renderPass);

        void Update(float frameTime);
    }
}
