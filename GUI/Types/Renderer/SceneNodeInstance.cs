namespace GUI.Types.Renderer;

internal class SceneNodeInstance : SceneNode, IRenderableMeshCollection
{
    public SceneNode InstancedNode { get; }

    public List<RenderableMesh> RenderableMeshes => InstancedNode is IRenderableMeshCollection renderableMeshCollection
        ? renderableMeshCollection.RenderableMeshes
        : IRenderableMeshCollection.Empty;

    public SceneNodeInstance(SceneNode instancedNode) : base(instancedNode.Scene)
    {
        InstancedNode = instancedNode;
    }

    public override void Update(Scene.UpdateContext context)
    {
    }

    public override void Render(Scene.RenderContext context)
    {
    }
}
