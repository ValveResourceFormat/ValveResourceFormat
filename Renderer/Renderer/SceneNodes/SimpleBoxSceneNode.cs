namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>Scene node that renders an opaque, axis-aligned colored box.</summary>
    public class SimpleBoxSceneNode : ShapeSceneNode
    {
        /// <inheritdoc/>
        public override bool IsTranslucent => false;

        /// <summary>Initializes a colored box centered on its local origin.</summary>
        /// <param name="scene">The scene that owns the node.</param>
        /// <param name="color">The box color.</param>
        /// <param name="scale">The full box size on each axis.</param>
        public SimpleBoxSceneNode(Scene scene, Color32 color, Vector3 scale)
            : base(scene, scale / -2, scale / 2, color)
        {
        }
    }
}
