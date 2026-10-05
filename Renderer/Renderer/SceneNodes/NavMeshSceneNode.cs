using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.NavMesh;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that visualizes navigation mesh areas and ladders.
    /// </summary>
    public class NavMeshSceneNode : ShapeSceneNode
    {
        private static readonly Color32 NavMeshColor = new(64, 32, 255, 100);
        private static readonly Color32 NavMeshLadderColor = new(16, 255, 32, 100);
        private static readonly Color32 FlowMapConnectionColor = new(255, 255, 255);

        private readonly int triangleIndexCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="NavMeshSceneNode"/> class from pre-built vertex and index lists.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="verts">The vertex data for the nav mesh geometry.</param>
        /// <param name="triangleInds">Triangle list indices for the filled areas.</param>
        /// <param name="lineInds">Line list indices for the area outlines.</param>
        public NavMeshSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> triangleInds, List<int> lineInds)
            : base(scene, verts, [.. triangleInds, .. lineInds])
        {
            triangleIndexCount = triangleInds.Count;
        }

        // Corners form a convex polygon in fan order: fill as a triangle list, outline as a line list.
        private static void AddPolygon(int firstVertexIndex, int cornerCount, List<int> triangleInds, List<int> lineInds)
        {
            if (cornerCount < 3)
            {
                return;
            }

            triangleInds.EnsureCapacity(triangleInds.Count + (cornerCount - 2) * 3);
            lineInds.EnsureCapacity(lineInds.Count + cornerCount * 2);

            for (var i = 1; i < cornerCount - 1; i++)
            {
                AddTriangle(triangleInds, firstVertexIndex, 0, i, i + 1);
            }

            for (var i = 0; i < cornerCount; i++)
            {
                lineInds.Add(firstVertexIndex + i);
                lineInds.Add(firstVertexIndex + (i + 1) % cornerCount);
            }
        }

        private static void AddLadder(NavMeshLadder ladder, List<SimpleVertexNormal> verts, List<int> triangleInds, List<int> lineInds, Color32 color, ref Vector3 minBounds, ref Vector3 maxBounds)
        {
            var normal = ladder.Direction switch
            {
                NavDirectionType.North => new Vector3(0, -1, 0),
                NavDirectionType.East => new Vector3(1, 0, 0),
                NavDirectionType.West => new Vector3(-1, 0, 0),
                _ => new Vector3(0, 1, 0),
            };
            var sidewaysVector = Vector3.Cross(normal, Vector3.UnitZ) * (ladder.Width / 2);

            var bottom1 = ladder.Bottom - sidewaysVector;
            var bottom2 = ladder.Bottom + sidewaysVector;
            var top1 = ladder.Top - sidewaysVector;
            var top2 = ladder.Top + sidewaysVector;

            minBounds = Vector3.Min(minBounds, bottom1);
            minBounds = Vector3.Min(minBounds, bottom2);
            maxBounds = Vector3.Max(maxBounds, top1);
            maxBounds = Vector3.Max(maxBounds, top2);

            var firstVertexIndex = verts.Count;

            verts.Add(new(bottom2, color, normal));
            verts.Add(new(bottom1, color, normal));
            verts.Add(new(top1, color, normal));
            verts.Add(new(top2, color, normal));

            AddPolygon(firstVertexIndex, 4, triangleInds, lineInds);
        }

        private static void AddArea(NavMeshArea area, List<SimpleVertexNormal> verts, List<int> triangleInds, List<int> lineInds, Color32 color, ref Vector3 minBounds, ref Vector3 maxBounds)
        {
            var firstVertexIndex = verts.Count;

            verts.EnsureCapacity(verts.Count + area.Corners.Length);

            foreach (var position in area.Corners)
            {
                minBounds = Vector3.Min(minBounds, position);
                maxBounds = Vector3.Max(maxBounds, position);

                verts.Add(new(position, color, Vector3.UnitZ));
            }

            AddPolygon(firstVertexIndex, area.Corners.Length, triangleInds, lineInds);
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.Translucent)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            renderShader.Use();

            renderShader.SetUniform("g_bTriplanarMapping", false);

            VertexArray.Bind(vao, renderShader);

            using var _ = GraphicsContext.RenderState.Scope(depthBias: 96, depthBiasClamp: 0.0005f);

            renderShader.SetUniform("g_bNormalShaded", false);
            GL.DrawElements(PrimitiveType.Lines, indexCount - triangleIndexCount, DrawElementsType.UnsignedInt, triangleIndexCount * sizeof(int));

            renderShader.SetUniform("g_bNormalShaded", true);
            GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, triangleIndexCount, DrawElementsType.UnsignedInt, 0, 1, Id);

        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        /// <summary>
        /// Adds a layer per hull of a <see cref="NavFlowMapFile"/> to the scene, drawing the areas of the
        /// <see cref="NavMeshFile"/> colored by the cluster they belong to, and lines between the centres of connected
        /// clusters.
        /// </summary>
        /// <param name="navFlowMapFile">The flow map to draw.</param>
        /// <param name="navMeshFile">The navigation mesh the flow map was built for.</param>
        /// <param name="scene">The scene to add the nodes to.</param>
        /// <param name="hullNames">Hull names from <see cref="NavHullNames.Read"/>, used in the layer names.</param>
        public static void AddFlowMapNodesToScene(NavFlowMapFile? navFlowMapFile, NavMeshFile? navMeshFile, Scene scene, IReadOnlyList<string> hullNames)
        {
            if (navFlowMapFile == null || navMeshFile == null || scene == null)
            {
                return;
            }

            var verts = new List<SimpleVertexNormal>();
            var triangleInds = new List<int>();
            var lineInds = new List<int>();

            foreach (var hull in navFlowMapFile.Hulls)
            {
                var layerName = $"Navigation flow map ({NavHullNames.GetName(hullNames, hull.HullIndex)})";
                var minBounds = new Vector3(float.MaxValue);
                var maxBounds = new Vector3(float.MinValue);
                var connectionLines = new List<SimpleVertex>();
                var connected = new HashSet<(int, int)>();

                foreach (var node in hull.Nodes)
                {
                    var color = WireframeSceneNode.GetIdColor((uint)node.Index) with { A = NavMeshColor.A };

                    foreach (var areaId in node.AreaIds)
                    {
                        if (navMeshFile.GetArea(areaId) is { } area)
                        {
                            AddArea(area, verts, triangleInds, lineInds, color, ref minBounds, ref maxBounds);
                        }
                    }

                    foreach (var connection in node.Connections)
                    {
                        var pair = (Math.Min(node.Index, connection.NodeIndex), Math.Max(node.Index, connection.NodeIndex));

                        if ((uint)connection.NodeIndex < (uint)hull.Nodes.Length && connected.Add(pair))
                        {
                            ShapeSceneNode.AddLine(connectionLines, node.Center, hull.Nodes[connection.NodeIndex].Center, FlowMapConnectionColor);
                        }
                    }
                }

                if (triangleInds.Count > 0)
                {
                    scene.Add(new NavMeshSceneNode(scene, verts, triangleInds, lineInds)
                    {
                        LayerName = layerName,
                        LocalBoundingBox = new AABB(minBounds, maxBounds),
                    }, false);
                }

                if (connectionLines.Count > 0)
                {
                    scene.Add(new LineSceneNode(scene, [.. connectionLines])
                    {
                        LayerName = layerName,
                    }, false);
                }

                verts.Clear();
                triangleInds.Clear();
                lineInds.Clear();
            }
        }

        /// <summary>
        /// Parses a <see cref="NavMeshFile"/> and adds a <see cref="NavMeshSceneNode"/> per hull and one for ladders to the scene.
        /// </summary>
        /// <param name="navMeshFile">The navigation mesh to draw.</param>
        /// <param name="scene">The scene to add the nodes to.</param>
        /// <param name="hullNames">Hull names from <see cref="NavHullNames.Read"/>, used in the layer names.</param>
        public static void AddNavNodesToScene(NavMeshFile? navMeshFile, Scene scene, IReadOnlyList<string> hullNames)
        {
            if (navMeshFile == null || scene == null)
            {
                return;
            }

            var verts = new List<SimpleVertexNormal>();
            var triangleInds = new List<int>();
            var lineInds = new List<int>();

            if (navMeshFile.GenerationParams != null)
            {
                for (byte i = 0; i < navMeshFile.GenerationParams.HullCount; i++)
                {
                    var hullAreas = navMeshFile.GetHullAreas(i);
                    if (hullAreas == null)
                    {
                        continue;
                    }

                    var minBounds = new Vector3(float.MinValue);
                    var maxBounds = new Vector3(float.MaxValue);
                    foreach (var area in hullAreas)
                    {
                        AddArea(area, verts, triangleInds, lineInds, NavMeshColor, ref minBounds, ref maxBounds);
                    }

                    var sceneNode = new NavMeshSceneNode(scene, verts, triangleInds, lineInds)
                    {
                        LayerName = $"Navigation mesh ({NavHullNames.GetName(hullNames, i)})",
                        LocalBoundingBox = new AABB(minBounds, maxBounds),
                    };
                    scene.Add(sceneNode, false);

                    verts.Clear();
                    triangleInds.Clear();
                    lineInds.Clear();
                }
            }

            if (navMeshFile.Ladders != null && navMeshFile.Ladders.Length > 0)
            {
                var minBounds = new Vector3(float.MinValue);
                var maxBounds = new Vector3(float.MaxValue);

                foreach (var ladder in navMeshFile.Ladders)
                {
                    AddLadder(ladder, verts, triangleInds, lineInds, NavMeshLadderColor, ref minBounds, ref maxBounds);
                }

                var laddersSceneNode = new NavMeshSceneNode(scene, verts, triangleInds, lineInds)
                {
                    LayerName = "Navigation mesh (ladders)",
                    LocalBoundingBox = new AABB(minBounds, maxBounds),
                };
                scene.Add(laddersSceneNode, false);
            }
        }
    }
}
