namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that renders a list of line segments.
    /// </summary>
    public class LineSceneNode : WireframeSceneNode
    {
        private readonly SimpleVertex[] vertices;

        /// <summary>
        /// Gets whether the lines are blended in the alpha of their colors, after the opaque scene, rather
        /// than drawn opaque with it.
        /// </summary>
        public bool Translucent { get; init; }

        /// <summary>
        /// Renders a single line segment.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="start">The start position of the line.</param>
        /// <param name="end">The end position of the line.</param>
        /// <param name="startColor">The color at the start of the line.</param>
        /// <param name="endColor">The color at the end of the line.</param>
        public LineSceneNode(Scene scene, Vector3 start, Vector3 end, Color32 startColor, Color32 endColor)
            : this(scene, [new(start, startColor), new(end, endColor)])
        {
        }

        /// <summary>
        /// Renders a list of line segments.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="vertices">Pairs of vertices, one pair per line segment.</param>
        public LineSceneNode(Scene scene, SimpleVertex[] vertices)
            : base(scene, nameof(LineSceneNode))
        {
            this.vertices = vertices;

            var boundsMin = vertices.Length > 0 ? vertices[0].Position : Vector3.Zero;
            var boundsMax = boundsMin;
            foreach (var vertex in vertices)
            {
                boundsMin = Vector3.Min(boundsMin, vertex.Position);
                boundsMax = Vector3.Max(boundsMax, vertex.Position);
            }

            LocalBoundingBox = new AABB(boundsMin, boundsMax);
        }

        /// <inheritdoc/>
        protected override bool IsTranslucent => Translucent;

        /// <inheritdoc/>
        protected override List<SimpleVertex> BuildLines() => [.. vertices];
    }
}
