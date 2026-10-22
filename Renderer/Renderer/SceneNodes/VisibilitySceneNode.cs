using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Debug scene node that draws voxel visibility cluster bounds as colored wireframe boxes.
    /// </summary>
    public class VisibilitySceneNode : WireframeSceneNode
    {
        private readonly record struct ClusterDrawRange(int Start, int Count, ushort ClusterId);

        private readonly IWorldVisibility voxelVisibility;
        private ClusterDrawRange[] clusterDrawRanges = [];

        /// <summary>
        /// Initializes a new <see cref="VisibilitySceneNode"/> from the given voxel visibility data.
        /// The cluster boxes are built when the node is first drawn.
        /// </summary>
        public VisibilitySceneNode(Scene scene, IWorldVisibility voxelVisibility)
            : base(scene, nameof(VisibilitySceneNode))
        {
            this.voxelVisibility = voxelVisibility;
            LocalBoundingBox = new AABB(voxelVisibility.MinBounds, voxelVisibility.MaxBounds);
        }

        /// <inheritdoc/>
        protected override List<SimpleVertex> BuildLines()
        {
            var clusterChildBounds = voxelVisibility.BuildClusterChildBounds();
            var boxCount = 0;

            foreach (var children in clusterChildBounds.Values)
            {
                boxCount += children.Count;
            }

            var vertices = new List<SimpleVertex>(boxCount * ShapeSceneNode.BoxLineVertexCount);
            var ranges = new List<ClusterDrawRange>(clusterChildBounds.Count);

            foreach (var (clusterId, children) in clusterChildBounds)
            {
                var start = vertices.Count;
                var color = GetIdColor(clusterId);

                foreach (var (min, max) in children)
                {
                    ShapeSceneNode.AddBox(vertices, new AABB(min, max), color);
                }

                if (vertices.Count > start)
                {
                    ranges.Add(new(start, vertices.Count - start, clusterId));
                }
            }

            clusterDrawRanges = [.. ranges];
            return vertices;
        }

        /// <inheritdoc/>
        protected override void DrawLines(Scene.RenderContext context)
        {
            var pvs = context.View?.Pvs ?? default;

            if (pvs.IsEmpty)
            {
                base.DrawLines(context);
                return;
            }

            foreach (var range in clusterDrawRanges)
            {
                if (range.ClusterId < (uint)(pvs.Length * 8) && MathUtils.GetBit(pvs.Span, range.ClusterId))
                {
                    DrawLines(range.Start, range.Count);
                }
            }
        }
    }
}
