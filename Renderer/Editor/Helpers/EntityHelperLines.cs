using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers
{
    /// <summary>
    /// Where a helper shape is drawn: solid where the scene does not hide it, over the scene in its color's
    /// alpha, or blended in its alpha where the scene does not hide it.
    /// </summary>
    [Flags]
    internal enum HelperPasses
    {
        /// <summary>Opaque, where the scene does not hide it.</summary>
        Visible = 1,

        /// <summary>Blended with its alpha, over the scene.</summary>
        Overlay = 2,

        /// <summary>Both.</summary>
        Both = Visible | Overlay,

        /// <summary>Blended with its alpha, where the scene does not hide it.</summary>
        Translucent = 4,
    }

    /// <summary>
    /// The vertices of helper shapes, split by how they are drawn: lines and triangles, each either where the scene
    /// does not hide them or over it.
    /// </summary>
    internal sealed class HelperVertices
    {
        /// <summary>Gets the lines drawn opaque where the scene does not hide them.</summary>
        public List<SimpleVertex> Lines { get; } = [];

        /// <summary>Gets the lines blended over the scene.</summary>
        public List<SimpleVertex> OverlayLines { get; } = [];

        /// <summary>Gets the triangles blended where the scene does not hide them.</summary>
        public List<SimpleVertex> Faces { get; } = [];

        /// <summary>Gets the triangles blended over the scene.</summary>
        public List<SimpleVertex> OverlayFaces { get; } = [];

        /// <summary>Gets the number of vertices in all the lists.</summary>
        public int Count => Lines.Count + OverlayLines.Count + Faces.Count + OverlayFaces.Count;

        /// <summary>Adds a line to the passes it is drawn in, opaque where the scene does not hide it.</summary>
        public void AddLine(Vector3 start, Vector3 end, Color32 color, HelperPasses passes)
        {
            if ((passes & HelperPasses.Visible) != 0)
            {
                ShapeSceneNode.AddLine(Lines, start, end, color with { A = 255 });
            }

            if ((passes & HelperPasses.Overlay) != 0)
            {
                ShapeSceneNode.AddLine(OverlayLines, start, end, color);
            }

            if ((passes & HelperPasses.Translucent) != 0)
            {
                ShapeSceneNode.AddLine(Lines, start, end, color);
            }
        }

        /// <summary>Empties all the lists.</summary>
        public void Clear()
        {
            Lines.Clear();
            OverlayLines.Clear();
            Faces.Clear();
            OverlayFaces.Clear();
        }
    }

    /// <summary>
    /// What a helper draws its shapes for a selected entity with: where the vertices go, and where the entity is.
    /// </summary>
    internal readonly struct SelectionShapes(HelperVertices output, Matrix4x4 placement, Matrix4x4 toWorld, bool isOnlySelected)
    {
        /// <summary>Gets whether the entity is the only thing selected.</summary>
        public bool IsOnlySelected => isOnlySelected;

        /// <summary>Gets the vertices to append to.</summary>
        public HelperVertices Output => output;

        /// <summary>Gets the origin and angles of the entity, without scale.</summary>
        public Matrix4x4 Placement => placement;

        /// <summary>Draws at the entity, turned with it.</summary>
        public EntityHelperLines Oriented(HelperPasses passes) => new(output, placement * toWorld, passes);

        /// <summary>Draws at the entity, along the world axes.</summary>
        public EntityHelperLines Aligned(HelperPasses passes) => new(output, Matrix4x4.CreateTranslation(placement.Translation) * toWorld, passes);

        /// <summary>Draws in world coordinates, wherever the entity is.</summary>
        public EntityHelperLines World(HelperPasses passes) => new(output, toWorld, passes);
    }

    /// <summary>
    /// Draws the shapes the helpers of a selected entity show, such as volume boxes, radius spheres, direction
    /// arrows and the outlines of lights. Shapes are built in the entity's space, X forward, and transformed
    /// into line vertices.
    /// </summary>
    internal readonly struct EntityHelperLines(HelperVertices output, Matrix4x4 transform, HelperPasses passes)
    {
        // What is selected is drawn in the selection wireframe color of the editor, which is a user setting;
        // this is its default
        internal static readonly Color32 SelectionColor = new(80, 200, 255);

        // The faces of a selected box are tinted
        private const byte BoxFaceAlpha = 32;
        private const byte BoxOverlayFaceAlpha = 8;

        /// <summary>Adds the shapes the helpers of a selected entity draw.</summary>
        /// <param name="output">Vertices to append to.</param>
        /// <param name="entity">The entity, with its helpers.</param>
        /// <param name="toWorld">Transform from the entity's scene to the viewer's world.</param>
        /// <param name="isOnlySelected">Whether the entity is the only thing selected.</param>
        /// <returns><see langword="true"/> when anything was drawn.</returns>
        public static bool TryAdd(HelperVertices output, IHelperOwner entity, in Matrix4x4 toWorld, bool isOnlySelected)
        {
            var count = output.Count;
            var shapes = new SelectionShapes(output, entity.RigidTransform, toWorld, isOnlySelected);

            foreach (var helper in entity.Helpers)
            {
                helper.AddSelectionShapes(shapes);
            }

            return output.Count > count;
        }

        /// <summary>
        /// A solid cone from a disc to its tip, its sides and the disc itself, drawn opaque over the scene.
        /// </summary>
        internal void AddSolidCone(Vector3 baseCenter, Vector3 tip, float radius, int segments, Color32 color)
        {
            var rotation = EntityTransformHelper.ForwardDirectionToRotationMatrix(Vector3.Normalize(tip - baseCenter));

            Vector3 GetRim(int i)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / segments);
                return baseCenter + Vector3.TransformNormal(new Vector3(0f, sin * radius, cos * radius), rotation);
            }

            var faces = With(HelperPasses.Overlay);

            for (var i = 0; i < segments; i++)
            {
                var rim = GetRim(i);
                var next = GetRim(i + 1);

                faces.AddFace(rim, next, tip, color);
                faces.AddFace(baseCenter, next, rim, color);
            }
        }

        /// <inheritdoc cref="AddFace(Vector3, Vector3, Vector3, Color32, Color32, Color32)"/>
        internal void AddFace(Vector3 a, Vector3 b, Vector3 c, Color32 color) => AddFace(a, b, c, color, color, color);

        /// <summary>
        /// A triangle seen from both sides, blended over the scene for <see cref="HelperPasses.Overlay"/> and
        /// where the scene does not hide it otherwise.
        /// </summary>
        internal void AddFace(Vector3 a, Vector3 b, Vector3 c, Color32 colorA, Color32 colorB, Color32 colorC)
        {
            var list = (passes & HelperPasses.Overlay) != 0 ? output.OverlayFaces : output.Faces;

            a = Vector3.Transform(a, transform);
            b = Vector3.Transform(b, transform);
            c = Vector3.Transform(c, transform);

            list.Add(new SimpleVertex(a, colorA));
            list.Add(new SimpleVertex(b, colorB));
            list.Add(new SimpleVertex(c, colorC));
            list.Add(new SimpleVertex(a, colorA));
            list.Add(new SimpleVertex(c, colorC));
            list.Add(new SimpleVertex(b, colorB));
        }

        internal void AddBox(in AABB box, Color32 color)
        {
            Span<Vector3> corners = stackalloc Vector3[8];
            BoxLines.GetCorners(box, corners);

            // Each edge joins two corners that differ along one axis
            for (var i = 0; i < corners.Length; i++)
            {
                for (var axis = 1; axis < corners.Length; axis <<= 1)
                {
                    if ((i & axis) == 0)
                    {
                        AddLine(corners[i], corners[i | axis], color);
                    }
                }
            }
        }

        /// <summary>
        /// Curves in four planes through the forward axis, 45 degrees apart: a lobe of what the light emits,
        /// or the hemisphere it reaches with the circle at its base.
        /// </summary>
        internal void AddEmissionCurves(float radius, float startDegrees, bool lobe, Color32 color)
        {
            const int Points = 128;

            for (var plane = 0; plane < 4; plane++)
            {
                var rotation = Matrix4x4.CreateRotationX(float.DegreesToRadians(startDegrees + plane * 45f));
                var previous = Vector3.Zero;

                for (var i = 0; i < Points; i++)
                {
                    var (sin, cos) = MathF.SinCos(i * MathF.PI / (Points - 1));
                    sin = MathF.Max(sin, 0f);

                    var side = cos * radius * (lobe ? MathF.Cbrt(sin) : 1f);
                    var point = Vector3.Transform(new Vector3(sin * radius, 0f, side), rotation);

                    if (i > 0)
                    {
                        AddLine(previous, point, color);
                    }

                    previous = point;
                }

                if (!lobe)
                {
                    AddLine(Vector3.Transform(new Vector3(0f, 0f, radius), rotation), Vector3.Transform(new Vector3(0f, 0f, -radius), rotation), color);
                }
            }

            if (!lobe)
            {
                AddCircle(Vector3.Zero, Vector3.UnitY * radius, Vector3.UnitZ * radius, Points, color);
            }
        }

        /// <summary>
        /// A superellipse in the plane facing +X, spanning <paramref name="halfSize"/> along Y and Z. An exponent of
        /// one is an ellipse, and lower ones square it off down to a rectangle at zero.
        /// </summary>
        private static Vector3 SuperellipsePoint(Vector3 center, Vector2 halfSize, float exponent, float angle)
        {
            var (sin, cos) = MathF.SinCos(angle);

            return center + new Vector3(
                0f,
                MathF.CopySign(MathF.Pow(MathF.Abs(sin), exponent), sin) * halfSize.X,
                MathF.CopySign(MathF.Pow(MathF.Abs(cos), exponent), cos) * halfSize.Y);
        }

        internal void AddSuperellipse(Vector3 center, Vector2 halfSize, float exponent, Color32 color)
        {
            const int Segments = 48;

            var previous = SuperellipsePoint(center, halfSize, exponent, 0f);

            for (var i = 1; i <= Segments; i++)
            {
                var point = SuperellipsePoint(center, halfSize, exponent, i * MathF.Tau / Segments);

                // A rectangle puts many points on each corner
                if (point != previous)
                {
                    AddLine(previous, point, color);
                }

                previous = point;
            }
        }

        internal void AddSuperellipseConnectors(Vector3 fromCenter, Vector2 fromHalfSize, Vector3 toCenter, Vector2 toHalfSize, float exponent, float startDegrees, float stepDegrees, Color32 color)
        {
            for (var angle = startDegrees; angle < 360f; angle += stepDegrees)
            {
                var radians = float.DegreesToRadians(angle);

                AddLine(SuperellipsePoint(fromCenter, fromHalfSize, exponent, radians), SuperellipsePoint(toCenter, toHalfSize, exponent, radians), color);
            }
        }

        /// <summary>
        /// A grid on the part of a sphere around Z between two angles from its +Z pole: lines along the meridians
        /// between rows, and along each row but the last.
        /// </summary>
        /// <param name="center">Center of the sphere.</param>
        /// <param name="radius">Radius of the sphere.</param>
        /// <param name="slices">Number of meridians.</param>
        /// <param name="stacks">Number of rows, the first at <paramref name="startAngle"/> and the last at <paramref name="endAngle"/>.</param>
        /// <param name="startAngle">Angle of the first row from +Z, in radians.</param>
        /// <param name="endAngle">Angle of the last row from +Z, in radians.</param>
        /// <param name="color">Color of the lines.</param>
        internal void AddSphereGrid(Vector3 center, float radius, int slices, int stacks, float startAngle, float endAngle, Color32 color)
        {
            Vector3 GetPoint(int stack, int slice)
            {
                var (sinTheta, cosTheta) = MathF.SinCos(stack / (float)(stacks - 1) * (endAngle - startAngle) + startAngle);
                var (sinPhi, cosPhi) = MathF.SinCos(slice / (float)slices * MathF.Tau);

                return center + new Vector3(cosPhi * sinTheta, sinPhi * sinTheta, cosTheta) * radius;
            }

            for (var stack = 0; stack < stacks - 1; stack++)
            {
                for (var slice = 0; slice < slices; slice++)
                {
                    var point = GetPoint(stack, slice);

                    AddLine(point, GetPoint(stack + 1, slice), color);
                    AddLine(point, GetPoint(stack, slice + 1), color);
                }
            }
        }

        // Twelve meridians through the poles on Z, and the rings of latitude between them
        internal void AddLatLongSphere(Vector3 center, float radius, Color32 color)
            => AddSphereGrid(center, radius, 12, 12, 0f, MathF.PI, color);

        // Three circles around the axes
        internal void AddGreatCircles(Vector3 center, float radius, Color32 color)
        {
            const int Segments = 48;

            AddCircle(center, Vector3.UnitX * radius, Vector3.UnitY * radius, Segments, color);
            AddCircle(center, Vector3.UnitX * radius, Vector3.UnitZ * radius, Segments, color);
            AddCircle(center, Vector3.UnitY * radius, Vector3.UnitZ * radius, Segments, color);
        }

        // A circle around center, spanned by two perpendicular radii
        internal void AddCircle(Vector3 center, Vector3 axisA, Vector3 axisB, int segments, Color32 color)
        {
            var previous = center + axisB;

            for (var i = 1; i <= segments; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / segments);
                var point = center + axisA * sin + axisB * cos;

                AddLine(previous, point, color);
                previous = point;
            }
        }

        internal EntityHelperLines With(HelperPasses newPasses) => new(output, transform, newPasses);

        /// <summary>Draws in a space placed in the current one.</summary>
        internal EntityHelperLines Transformed(in Matrix4x4 local) => new(output, local * transform, passes);

        // The faces of a box, tinted where seen and fainter over the scene, seen from both sides
        internal void AddBoxFaces(in AABB box, Color32 color)
        {
            Span<Vector3> corners = stackalloc Vector3[8];
            BoxLines.GetCorners(box, corners);

            // Corner indices of each face
            ReadOnlySpan<int> faces =
            [
                0, 2, 3, 1, // -Z
                4, 5, 7, 6, // +Z
                0, 1, 5, 4, // -Y
                2, 6, 7, 3, // +Y
                0, 4, 6, 2, // -X
                1, 3, 7, 5, // +X
            ];

            var visible = With(HelperPasses.Visible);
            var overlay = With(HelperPasses.Overlay);
            var faceColor = color with { A = BoxFaceAlpha };
            var overlayColor = color with { A = BoxOverlayFaceAlpha };

            for (var i = 0; i < faces.Length; i += 4)
            {
                var (a, b, c, d) = (corners[faces[i]], corners[faces[i + 1]], corners[faces[i + 2]], corners[faces[i + 3]]);

                visible.AddFace(a, b, c, faceColor);
                visible.AddFace(a, c, d, faceColor);
                overlay.AddFace(a, b, c, overlayColor);
                overlay.AddFace(a, c, d, overlayColor);
            }
        }

        internal void AddLine(Vector3 from, Vector3 to, Color32 color)
            => output.AddLine(Vector3.Transform(from, transform), Vector3.Transform(to, transform), color, passes);
    }
}
