using System.Linq;
using ValveResourceFormat.NavMesh;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Debug scene node that draws navigation space volumes as colored wireframe convex hulls.
    /// </summary>
    public class NavSpaceSceneNode : WireframeSceneNode
    {
        private static readonly Color32 TransitionColor = new(255, 255, 0);
        private static readonly Color32 CellColor = new(240, 230, 140);

        private readonly Func<List<SimpleVertex>> buildLines;

        private NavSpaceSceneNode(Scene scene, AABB bounds, Func<List<SimpleVertex>> buildLines)
            : base(scene, nameof(NavSpaceSceneNode))
        {
            this.buildLines = buildLines;
            LocalBoundingBox = bounds;
        }

        /// <inheritdoc/>
        protected override List<SimpleVertex> BuildLines() => buildLines();

        /// <summary>
        /// Adds nodes for the blocks, the transitions and the grid cells holding blocks of a <see cref="NavSpaceFile"/>
        /// to the scene, each as its own layer. Each block is colored by its id.
        /// </summary>
        public static void AddToScene(NavSpaceFile? navSpaceFile, Scene scene)
        {
            if (navSpaceFile == null || scene == null || !navSpaceFile.Blocks.Any())
            {
                return;
            }

            var (sizeX, sizeY, sizeZ) = navSpaceFile.GridSize;
            var bounds = new AABB(navSpaceFile.Bounds.Min, navSpaceFile.Bounds.Min + new Vector3(sizeX, sizeY, sizeZ) * navSpaceFile.CellSize);

            AddLayer(scene, "Navigation space (blocks)", bounds, () => BuildBlockLines(navSpaceFile));

            if (navSpaceFile.Transitions.Length > 0)
            {
                AddLayer(scene, "Navigation space (transitions)", bounds, () => BuildTransitionLines(navSpaceFile));
            }

            AddLayer(scene, "Navigation space (grid cells)", bounds, () => BuildCellLines(navSpaceFile));
        }

        private static List<SimpleVertex> BuildBlockLines(NavSpaceFile navSpaceFile)
        {
            var vertices = new List<SimpleVertex>();
            var edges = new List<int>();

            foreach (var block in navSpaceFile.Blocks)
            {
                AddConvexHull(vertices, block.Vertices, GetIdColor(block.Id), edges);
            }

            return vertices;
        }

        private static List<SimpleVertex> BuildTransitionLines(NavSpaceFile navSpaceFile)
        {
            var vertices = new List<SimpleVertex>();
            var edges = new List<int>();

            foreach (var transition in navSpaceFile.Transitions)
            {
                AddConvexHull(vertices, transition.Vertices, TransitionColor, edges);
            }

            return vertices;
        }

        private static List<SimpleVertex> BuildCellLines(NavSpaceFile navSpaceFile)
        {
            var vertices = new List<SimpleVertex>();
            var (sizeX, sizeY, _) = navSpaceFile.GridSize;

            for (var cell = 0; cell < navSpaceFile.Cells.Length; cell++)
            {
                if (navSpaceFile.Cells[cell].Length == 0)
                {
                    continue;
                }

                var coordinate = new Vector3(cell % sizeX, cell / sizeX % sizeY, cell / (sizeX * sizeY));
                var min = navSpaceFile.Bounds.Min + coordinate * navSpaceFile.CellSize;

                ShapeSceneNode.AddBox(vertices, new AABB(min + Vector3.One, min + new Vector3(navSpaceFile.CellSize - 1f)), CellColor);
            }

            return vertices;
        }

        private static void AddConvexHull(List<SimpleVertex> vertices, Vector3[] points, Color32 color, List<int> edges)
        {
            edges.Clear();
            ConvexHull.GetEdges(points, edges);

            for (var i = 0; i < edges.Count; i += 2)
            {
                ShapeSceneNode.AddLine(vertices, points[edges[i]], points[edges[i + 1]], color);
            }
        }

        private static void AddLayer(Scene scene, string layerName, AABB bounds, Func<List<SimpleVertex>> buildLines)
        {
            scene.Add(new NavSpaceSceneNode(scene, bounds, buildLines)
            {
                LayerName = layerName,
            }, false);
        }
    }
}
