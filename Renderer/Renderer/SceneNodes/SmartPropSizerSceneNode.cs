using ValveResourceFormat.ResourceTypes.SmartProps;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Renders a SmartProp sizer as a translucent volume or plane with solid face arrows.
    /// </summary>
    public class SmartPropSizerSceneNode : SmartPropSolidWidgetSceneNode
    {
        private const float ArrowLength = 14f;
        private const float ArrowShaftRadius = 0.7f;
        private const float ArrowHeadLength = 7f;
        private const float ArrowHeadRadius = 2.5f;
        private const int ArrowSegments = 12;

        private static readonly Color32 FillColor3D = new(0.22f, 0.65f, 0.95f, 0.30f);
        private static readonly Color32 FillColor2D = new(0.22f, 0.65f, 0.95f, 0.35f);
        private static readonly Color32 OutlineColor = new(0.35f, 0.85f, 1f, 0.95f);
        private static readonly Color32 Red = new(0.90f, 0.15f, 0.15f, 1f);
        private static readonly Color32 Green = new(0.15f, 0.85f, 0.20f, 1f);
        private static readonly Color32 Blue = new(0.15f, 0.35f, 0.95f, 1f);

        /// <inheritdoc/>
        public override bool IsTranslucent => true;

        /// <inheritdoc/>
        protected override bool Shaded => false;

        /// <inheritdoc/>
        protected override bool DepthTested => false;

        /// <summary>Initializes the node for an evaluated sizer widget.</summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="widget">The evaluated sizer widget.</param>
        public SmartPropSizerSceneNode(Scene scene, in SmartPropSizerWidget widget)
            : this(scene, widget, BuildVolumeGeometry(widget), BuildArrowGeometry(widget))
        {
        }

        private SmartPropSizerSceneNode(
            Scene scene,
            in SmartPropSizerWidget widget,
            (List<SimpleVertexNormal> Vertices, List<int> Indices) volumeGeometry,
            (List<SimpleVertexNormal> Vertices, List<int> Indices) arrowGeometry)
            : base(
                scene,
                volumeGeometry.Vertices,
                volumeGeometry.Indices,
                BuildGuideGeometry(widget),
                SmartPropTransform.TransformPoint(widget.WorldMatrix, widget.MaxBounds),
                widget.Name,
                OutlineColor,
                arrowGeometry.Vertices,
                arrowGeometry.Indices)
        {
        }

        /// <summary>Builds the filled volume and arrow handles in world space.</summary>
        /// <param name="widget">The evaluated sizer widget.</param>
        /// <returns>Solid vertices and triangle indices.</returns>
        public static (List<SimpleVertexNormal> Vertices, List<int> Indices) BuildSolidGeometry(in SmartPropSizerWidget widget)
        {
            var volumeGeometry = BuildVolumeGeometry(widget);
            var arrowGeometry = BuildArrowGeometry(widget);
            AppendGeometry(volumeGeometry.Vertices, volumeGeometry.Indices, arrowGeometry.Vertices, arrowGeometry.Indices);
            return volumeGeometry;
        }

        /// <summary>Builds the translucent sizer volume or plane in world space.</summary>
        /// <param name="widget">The evaluated sizer widget.</param>
        /// <returns>Solid vertices and triangle indices.</returns>
        public static (List<SimpleVertexNormal> Vertices, List<int> Indices) BuildVolumeGeometry(in SmartPropSizerWidget widget)
        {
            List<SimpleVertexNormal> vertices = [];
            List<int> indices = [];
            var activeCount = GetActiveAxisCount(widget.ActiveAxes);

            if (activeCount == 3)
            {
                AddVolume(vertices, indices, widget);
            }
            else if (activeCount == 2)
            {
                AddPlane(vertices, indices, widget);
            }

            return (vertices, indices);
        }

        /// <summary>Builds the opaque solid arrow handles in world space.</summary>
        /// <param name="widget">The evaluated sizer widget.</param>
        /// <returns>Solid vertices and triangle indices.</returns>
        public static (List<SimpleVertexNormal> Vertices, List<int> Indices) BuildArrowGeometry(in SmartPropSizerWidget widget)
        {
            List<SimpleVertexNormal> vertices = [];
            List<int> indices = [];

            var (forward, left, up) = BasisRows(widget.WorldMatrix);
            AddArrow(vertices, indices, widget, forward, left, up, widget.Handles.MaxX, 1, 0, 0, Red);
            AddArrow(vertices, indices, widget, forward, left, up, widget.Handles.MinX, -1, 0, 0, Red);
            AddArrow(vertices, indices, widget, forward, left, up, widget.Handles.MaxY, 0, 1, 0, Green);
            AddArrow(vertices, indices, widget, forward, left, up, widget.Handles.MinY, 0, -1, 0, Green);
            AddArrow(vertices, indices, widget, forward, left, up, widget.Handles.MaxZ, 0, 0, 1, Blue);
            AddArrow(vertices, indices, widget, forward, left, up, widget.Handles.MinZ, 0, 0, -1, Blue);

            return (vertices, indices);
        }

        /// <summary>Builds the intentional outline or axis guide for the sizer.</summary>
        /// <param name="widget">The evaluated sizer widget.</param>
        /// <returns>Line vertices, two per segment.</returns>
        public static List<SimpleVertex> BuildGuideGeometry(in SmartPropSizerWidget widget)
        {
            List<SimpleVertex> vertices = [];
            var activeCount = GetActiveAxisCount(widget.ActiveAxes);
            if (activeCount == 0)
            {
                return vertices;
            }

            if (activeCount == 1)
            {
                var (from, to) = widget.ActiveAxes.X
                    ? (new Vector3(widget.MinBounds.X, 0f, 0f), new Vector3(widget.MaxBounds.X, 0f, 0f))
                    : widget.ActiveAxes.Y
                        ? (new Vector3(0f, widget.MinBounds.Y, 0f), new Vector3(0f, widget.MaxBounds.Y, 0f))
                        : (new Vector3(0f, 0f, widget.MinBounds.Z), new Vector3(0f, 0f, widget.MaxBounds.Z));
                AddGuideLine(
                    vertices,
                    SmartPropTransform.TransformPoint(widget.WorldMatrix, from),
                    SmartPropTransform.TransformPoint(widget.WorldMatrix, to),
                    OutlineColor);
                return vertices;
            }

            var corners = activeCount == 3
                ? GetBoxCorners(widget.MinBounds, widget.MaxBounds)
                : GetPlaneCorners(widget);

            if (activeCount == 3)
            {
                ReadOnlySpan<int> edges =
                [
                    0, 1, 1, 2, 2, 3, 3, 0,
                    4, 5, 5, 6, 6, 7, 7, 4,
                    0, 4, 1, 5, 2, 6, 3, 7,
                ];
                AddTransformedEdges(vertices, widget.WorldMatrix, corners, edges);
            }
            else
            {
                ReadOnlySpan<int> edges = [0, 1, 1, 2, 2, 3, 3, 0];
                AddTransformedEdges(vertices, widget.WorldMatrix, corners, edges);
            }

            return vertices;
        }

        /// <summary>Builds a line representation of the filled and guide geometry.</summary>
        /// <param name="widget">The evaluated sizer widget.</param>
        /// <returns>Line vertices, two per segment.</returns>
        public static List<SimpleVertex> BuildGeometry(in SmartPropSizerWidget widget)
        {
            var geometry = BuildSolidGeometry(widget);
            var lines = BuildGuideGeometry(widget);
            lines.AddRange(BuildTriangleEdges(geometry.Vertices, geometry.Indices));
            return lines;
        }

        private static void AddVolume(
            List<SimpleVertexNormal> vertices,
            List<int> indices,
            in SmartPropSizerWidget widget)
        {
            var corners = GetBoxCorners(widget.MinBounds, widget.MaxBounds);
            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = SmartPropTransform.TransformPoint(widget.WorldMatrix, corners[i]);
            }

            AddSolidQuad(vertices, indices, corners[0], corners[1], corners[2], corners[3], FillColor3D);
            AddSolidQuad(vertices, indices, corners[4], corners[7], corners[6], corners[5], FillColor3D);
            AddSolidQuad(vertices, indices, corners[0], corners[4], corners[5], corners[1], FillColor3D);
            AddSolidQuad(vertices, indices, corners[3], corners[2], corners[6], corners[7], FillColor3D);
            AddSolidQuad(vertices, indices, corners[0], corners[3], corners[7], corners[4], FillColor3D);
            AddSolidQuad(vertices, indices, corners[1], corners[5], corners[6], corners[2], FillColor3D);
        }

        private static void AddPlane(
            List<SimpleVertexNormal> vertices,
            List<int> indices,
            in SmartPropSizerWidget widget)
        {
            var corners = GetPlaneCorners(widget);
            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = SmartPropTransform.TransformPoint(widget.WorldMatrix, corners[i]);
            }

            AddSolidQuad(vertices, indices, corners[0], corners[1], corners[2], corners[3], FillColor2D);
        }

        private static void AddArrow(
            List<SimpleVertexNormal> vertices,
            List<int> indices,
            in SmartPropSizerWidget widget,
            Vector3 forward,
            Vector3 left,
            Vector3 up,
            bool enabled,
            int x,
            int y,
            int z,
            Color32 color)
        {
            if (!enabled)
            {
                return;
            }

            var localPosition = new Vector3(
                x < 0 ? widget.MinBounds.X : x > 0 ? widget.MaxBounds.X : (widget.MinBounds.X + widget.MaxBounds.X) * 0.5f,
                y < 0 ? widget.MinBounds.Y : y > 0 ? widget.MaxBounds.Y : (widget.MinBounds.Y + widget.MaxBounds.Y) * 0.5f,
                z < 0 ? widget.MinBounds.Z : z > 0 ? widget.MaxBounds.Z : (widget.MinBounds.Z + widget.MaxBounds.Z) * 0.5f);
            var origin = SmartPropTransform.TransformPoint(widget.WorldMatrix, localPosition);
            var direction = NormalizeOr((forward * x) + (left * y) + (up * z), Vector3.UnitZ);
            AddSolidArrow(vertices, indices, origin, direction, color);
        }

        private static void AddSolidArrow(
            List<SimpleVertexNormal> vertices,
            List<int> indices,
            Vector3 origin,
            Vector3 direction,
            Color32 color)
        {
            var shaftLength = ArrowLength - ArrowHeadLength;
            var (axisU, axisV) = OrthonormalBasis(direction);

            for (var i = 0; i < ArrowSegments; i++)
            {
                var angle1 = i * MathF.Tau / ArrowSegments;
                var angle2 = (i + 1) * MathF.Tau / ArrowSegments;
                var radial1 = (axisU * MathF.Cos(angle1)) + (axisV * MathF.Sin(angle1));
                var radial2 = (axisU * MathF.Cos(angle2)) + (axisV * MathF.Sin(angle2));
                var bottom1 = origin + (radial1 * ArrowShaftRadius);
                var bottom2 = origin + (radial2 * ArrowShaftRadius);
                var top1 = bottom1 + (direction * shaftLength);
                var top2 = bottom2 + (direction * shaftLength);
                AddSolidQuad(vertices, indices, bottom1, bottom2, top2, top1, color);
            }

            var coneBase = origin + (direction * shaftLength);
            var tip = origin + (direction * ArrowLength);
            for (var i = 0; i < ArrowSegments; i++)
            {
                var angle1 = i * MathF.Tau / ArrowSegments;
                var angle2 = (i + 1) * MathF.Tau / ArrowSegments;
                var radial1 = (axisU * MathF.Cos(angle1)) + (axisV * MathF.Sin(angle1));
                var radial2 = (axisU * MathF.Cos(angle2)) + (axisV * MathF.Sin(angle2));
                var base1 = coneBase + (radial1 * ArrowHeadRadius);
                var base2 = coneBase + (radial2 * ArrowHeadRadius);
                AddSolidTriangle(vertices, indices, coneBase, base2, base1, color);
                AddSolidTriangle(vertices, indices, tip, base1, base2, color);
            }
        }

        private static void AppendGeometry(
            List<SimpleVertexNormal> targetVertices,
            List<int> targetIndices,
            List<SimpleVertexNormal> sourceVertices,
            List<int> sourceIndices)
        {
            var baseVertex = targetVertices.Count;
            targetVertices.AddRange(sourceVertices);
            foreach (var index in sourceIndices)
            {
                targetIndices.Add(baseVertex + index);
            }
        }

        private static void AddTransformedEdges(
            List<SimpleVertex> vertices,
            Matrix4x4 worldMatrix,
            ReadOnlySpan<Vector3> corners,
            ReadOnlySpan<int> edges)
        {
            for (var i = 0; i < edges.Length; i += 2)
            {
                AddGuideLine(
                    vertices,
                    SmartPropTransform.TransformPoint(worldMatrix, corners[edges[i]]),
                    SmartPropTransform.TransformPoint(worldMatrix, corners[edges[i + 1]]),
                    OutlineColor);
            }
        }

        private static Vector3[] GetBoxCorners(Vector3 minBounds, Vector3 maxBounds)
            =>
            [
                new(minBounds.X, minBounds.Y, minBounds.Z),
                new(maxBounds.X, minBounds.Y, minBounds.Z),
                new(maxBounds.X, maxBounds.Y, minBounds.Z),
                new(minBounds.X, maxBounds.Y, minBounds.Z),
                new(minBounds.X, minBounds.Y, maxBounds.Z),
                new(maxBounds.X, minBounds.Y, maxBounds.Z),
                new(maxBounds.X, maxBounds.Y, maxBounds.Z),
                new(minBounds.X, maxBounds.Y, maxBounds.Z),
            ];

        private static Vector3[] GetPlaneCorners(in SmartPropSizerWidget widget)
        {
            if (!widget.ActiveAxes.X)
            {
                return
                [
                    new(widget.MinBounds.X, widget.MinBounds.Y, widget.MinBounds.Z),
                    new(widget.MinBounds.X, widget.MaxBounds.Y, widget.MinBounds.Z),
                    new(widget.MinBounds.X, widget.MaxBounds.Y, widget.MaxBounds.Z),
                    new(widget.MinBounds.X, widget.MinBounds.Y, widget.MaxBounds.Z),
                ];
            }

            if (!widget.ActiveAxes.Y)
            {
                return
                [
                    new(widget.MinBounds.X, widget.MinBounds.Y, widget.MinBounds.Z),
                    new(widget.MaxBounds.X, widget.MinBounds.Y, widget.MinBounds.Z),
                    new(widget.MaxBounds.X, widget.MinBounds.Y, widget.MaxBounds.Z),
                    new(widget.MinBounds.X, widget.MinBounds.Y, widget.MaxBounds.Z),
                ];
            }

            return
            [
                new(widget.MinBounds.X, widget.MinBounds.Y, widget.MinBounds.Z),
                new(widget.MaxBounds.X, widget.MinBounds.Y, widget.MinBounds.Z),
                new(widget.MaxBounds.X, widget.MaxBounds.Y, widget.MinBounds.Z),
                new(widget.MinBounds.X, widget.MaxBounds.Y, widget.MinBounds.Z),
            ];
        }

        private static int GetActiveAxisCount(SmartPropSizerAxes axes)
            => (axes.X ? 1 : 0) + (axes.Y ? 1 : 0) + (axes.Z ? 1 : 0);
    }
}
