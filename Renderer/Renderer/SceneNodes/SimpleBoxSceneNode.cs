namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>An opaque box marking a point.</summary>
    public class SimpleBoxSceneNode : ShapeSceneNode
    {
        /// <inheritdoc/>
        public override bool IsTranslucent => false;

        /// <inheritdoc/>
        public override bool IsPointMarker => true;

        /// <summary>Initializes a box centred on the node's origin.</summary>
        /// <param name="scene">The scene the node belongs to.</param>
        /// <param name="color">The box colour.</param>
        /// <param name="scale">The size of the box along each axis.</param>
        public SimpleBoxSceneNode(Scene scene, Color32 color, Vector3 scale)
            : base(scene, scale / -2, scale / 2, color)
        {
        }
    }
}
