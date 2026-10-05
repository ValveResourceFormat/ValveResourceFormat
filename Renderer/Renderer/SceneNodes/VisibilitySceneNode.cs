using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Debug scene node that draws voxel visibility cluster bounds as colored wireframe boxes.
    /// </summary>
    public class VisibilitySceneNode : WireframeSceneNode
    {
        private readonly record struct ClusterDrawRange(int Start, int Count, ushort ClusterId);

        private readonly ClusterDrawRange[] clusterDrawRanges;

        /// <summary>
        /// Initializes a new <see cref="VisibilitySceneNode"/> from the given voxel visibility data.
        /// </summary>
        public VisibilitySceneNode(Scene scene, IWorldVisibility voxelVisibility)
            : this(scene, voxelVisibility, BuildClusterLines(voxelVisibility))
        {
        }

        private VisibilitySceneNode(Scene scene, IWorldVisibility voxelVisibility, (List<SimpleVertex> Vertices, ClusterDrawRange[] Ranges) lines)
            : base(scene, lines.Vertices, nameof(VisibilitySceneNode))
        {
            clusterDrawRanges = lines.Ranges;
            LocalBoundingBox = new AABB(voxelVisibility.MinBounds, voxelVisibility.MaxBounds);
        }

        private static (List<SimpleVertex> Vertices, ClusterDrawRange[] Ranges) BuildClusterLines(IWorldVisibility voxelVisibility)
        {
            var vertices = new List<SimpleVertex>();
            var ranges = new List<ClusterDrawRange>();

            foreach (var (clusterId, children) in voxelVisibility.BuildClusterChildBounds())
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

            return (vertices, [.. ranges]);
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
