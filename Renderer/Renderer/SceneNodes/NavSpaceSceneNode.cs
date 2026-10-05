using ValveResourceFormat.NavMesh;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Debug scene node that draws navigation space volumes as colored wireframe convex hulls.
    /// </summary>
    public class NavSpaceSceneNode : WireframeSceneNode
    {
        private static readonly Color32 TransitionColor = new(255, 255, 0);
        private static readonly Color32 CellWithBlocksColor = new(240, 230, 140);
        private static readonly Color32 EmptyCellColor = new(138, 43, 226);

        private NavSpaceSceneNode(Scene scene, List<SimpleVertex> vertices, AABB bounds)
            : base(scene, vertices, nameof(NavSpaceSceneNode))
        {
            LocalBoundingBox = bounds;
        }

        /// <summary>
        /// Adds nodes for the blocks, the transitions and the grid cells of a <see cref="NavSpaceFile"/> to the scene,
        /// each as its own layer. Each block is colored by its id.
        /// </summary>
        public static void AddToScene(NavSpaceFile? navSpaceFile, Scene scene)
        {
            if (navSpaceFile == null || scene == null)
            {
                return;
            }

            var edges = new List<int>();

            var blocks = new List<SimpleVertex>();
            foreach (var block in navSpaceFile.Blocks)
            {
                AddConvexHull(blocks, block.Vertices, GetIdColor(block.Id), edges);
            }

            var transitions = new List<SimpleVertex>();
            foreach (var transition in navSpaceFile.Transitions)
            {
                AddConvexHull(transitions, transition.Vertices, TransitionColor, edges);
            }

            var cells = new List<SimpleVertex>();
            var (sizeX, sizeY, sizeZ) = navSpaceFile.GridSize;

            for (var cell = 0; cell < navSpaceFile.Cells.Length; cell++)
            {
                var coordinate = new Vector3(cell % sizeX, cell / sizeX % sizeY, cell / (sizeX * sizeY));
                var min = navSpaceFile.Bounds.Min + coordinate * navSpaceFile.CellSize;
                var color = navSpaceFile.Cells[cell].Length > 0 ? CellWithBlocksColor : EmptyCellColor;

                ShapeSceneNode.AddBox(cells, new AABB(min + Vector3.One, min + new Vector3(navSpaceFile.CellSize - 1f)), color);
            }

            var bounds = new AABB(navSpaceFile.Bounds.Min, navSpaceFile.Bounds.Min + new Vector3(sizeX, sizeY, sizeZ) * navSpaceFile.CellSize);

            AddLayer(scene, "Navigation space (blocks)", blocks, bounds);
            AddLayer(scene, "Navigation space (transitions)", transitions, bounds);
            AddLayer(scene, "Navigation space (grid cells)", cells, bounds);
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

        private static void AddLayer(Scene scene, string layerName, List<SimpleVertex> vertices, AABB bounds)
        {
            if (vertices.Count == 0)
            {
                return;
            }

            scene.Add(new NavSpaceSceneNode(scene, vertices, bounds)
            {
                LayerName = layerName,
            }, false);
        }
    }
}
