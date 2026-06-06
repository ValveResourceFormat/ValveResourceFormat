namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that renders a solid axis-aligned box using a single vertex color.
    /// </summary>
    public class SimpleBoxSceneNode : ShapeSceneNode
    {
        /// <inheritdoc/>
        public override bool IsTranslucent => false;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleBoxSceneNode"/> class.
        /// </summary>
        /// <param name="scene">Scene that owns this node.</param>
        /// <param name="color">Box vertex color.</param>
        /// <param name="scale">World-space box size before transform.</param>
        public SimpleBoxSceneNode(Scene scene, Color32 color, Vector3 scale)
            : base(scene, scale / -2, scale / 2, color)
        {
        }
    }
}
