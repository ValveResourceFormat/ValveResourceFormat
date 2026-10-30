using static ValveResourceFormat.Source1.Bsp.BspMeshBuilder;

namespace ValveResourceFormat.Source1.Bsp
{
    /// <summary>
    /// Projects overlays onto the triangles of the faces they list. Each triangle is projected along the overlay normal
    /// into the overlay plane, the overlay quad is clipped to it, and the clipped polygon takes its vertex data from
    /// the triangle by barycentric weights, so overlays follow displacements and carry the lightmap UVs of their face.
    /// </summary>
    internal static class BspOverlayBuilder
    {
        private const SurfFlags SkippedFlags = SurfFlags.NoDraw | SurfFlags.Hint | SurfFlags.Skip | SurfFlags.Trigger | SurfFlags.Sky | SurfFlags.Sky2D;
        private const float Epsilon = 0.0001f;

        private struct ClipVertex
        {
            public Vector3 Position;
        }

        /// <param name="context">The build context of the map.</param>
        /// <param name="firstFace">The first face of the model.</param>
        /// <param name="endFace">The face after the last face of the model.</param>
        /// <param name="mesh">The model mesh that receives the overlays.</param>
        /// <param name="getDecalSize">Gets the world size of an <c>infodecal</c> texture, or null when the material is unknown. Decals are skipped when this is null.</param>
        public static void AddOverlays(BuildContext context, int firstFace, int endFace, BspModelMesh mesh, Func<string, Vector2?>? getDecalSize = null)
        {
            var bsp = context.Bsp;
            var scratch = new Scratch();

            foreach (var overlay in bsp.Overlays)
            {
                if (!TryGetTextureMaterial(bsp, overlay.TexInfo, out var overlayTexInfo, out var material))
                {
                    continue;
                }

                BspMaterialMesh? target = null;

                foreach (var faceIndex in overlay.Faces)
                {
                    if (faceIndex >= firstFace && faceIndex < endFace)
                    {
                        target = ProjectOntoFace(context, overlay, faceIndex, overlayTexInfo, material, target, scratch);
                    }
                }

                if (target != null && target.IndexList.Count > 0)
                {
                    mesh.Overlays.Add(target);
                }
            }

            // The engine puts a decal on the world only
            if (getDecalSize != null && mesh.ModelIndex == 0)
            {
                AddDecals(context, firstFace, endFace, mesh, getDecalSize, scratch);
            }
        }

        private sealed class Scratch
        {
            public List<BspVertex> Vertices { get; } = [];
            public List<int> Indices { get; } = [];
            public List<ClipVertex> Polygon { get; } = [];
            public List<ClipVertex> Clipped { get; } = [];
        }

        private static BspMaterialMesh? ProjectOntoFace(BuildContext context, BspOverlay overlay, int faceIndex,
            BspTextureInfo overlayTexInfo, string material, BspMaterialMesh? target, Scratch scratch)
        {
            var face = context.Faces[faceIndex];

            if (!TryGetFaceMaterial(context.Bsp, face, out var faceTexInfo, out _) || (faceTexInfo.Flags & SkippedFlags) != 0)
            {
                return target;
            }

            var vertices = scratch.Vertices;
            var indices = scratch.Indices;

            vertices.Clear();
            indices.Clear();
            AppendFace(context, faceIndex, vertices, indices);

            if (indices.Count == 0)
            {
                return target;
            }

            target ??= CreateTarget(overlay, overlayTexInfo, material);

            if (face.HasLightmap && context.Layout.IsBumped(faceIndex))
            {
                target.Flags |= BspMeshFlags.Bumped;
            }

            for (var i = 0; i + 2 < indices.Count; i += 3)
            {
                ClipTriangle(overlay, vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]], target, scratch.Polygon, scratch.Clipped);
            }

            return target;
        }

        // How far from the decal origin a face plane may be for the decal to land on it
        private const float DecalDistance = 4f;

        private static void AddDecals(BuildContext context, int firstFace, int endFace, BspModelMesh mesh, Func<string, Vector2?> getDecalSize, Scratch scratch)
        {
            var bsp = context.Bsp;
            var nextIndex = bsp.Overlays.Count;

            foreach (var entity in bsp.Entities)
            {
                if (entity.ClassName != "infodecal")
                {
                    continue;
                }

                var material = entity.GetString("texture");

                if (material.Length == 0 || getDecalSize(material) is not { } size)
                {
                    continue;
                }

                var origin = entity.Origin;
                BspMaterialMesh? target = null;

                for (var faceIndex = firstFace; faceIndex < endFace; faceIndex++)
                {
                    var face = context.Faces[faceIndex];

                    if (face.DispInfo >= 0 || face.NumEdges < 3 || face.PlaneNum >= bsp.Planes.Length)
                    {
                        continue;
                    }

                    var plane = bsp.Planes[face.PlaneNum];

                    if (MathF.Abs(Vector3.Dot(plane.Normal, origin) - plane.Dist) > DecalDistance)
                    {
                        continue;
                    }

                    if (!TryGetFaceMaterial(bsp, face, out var faceTexInfo, out _) || (faceTexInfo.Flags & (SkippedFlags | SurfFlags.NoDecals)) != 0)
                    {
                        continue;
                    }

                    var normal = GetFaceNormal(bsp, face);
                    var overlay = CreateDecalOverlay(nextIndex, origin, normal, size);

                    target = ProjectOntoFace(context, overlay, faceIndex, default, material, target, scratch);
                }

                if (target != null && target.IndexList.Count > 0)
                {
                    target.OverlayIndex = nextIndex++;
                    mesh.Overlays.Add(target);
                }
            }
        }

        // Faces wind clockwise when seen from the front
        private static Vector3 GetFaceNormal(BspFile bsp, BspFace face)
        {
            var first = bsp.GetVertexFromSurfEdge(face.FirstEdge);
            var second = bsp.GetVertexFromSurfEdge(face.FirstEdge + 1);
            var third = bsp.GetVertexFromSurfEdge(face.FirstEdge + 2);
            var normal = Vector3.Cross(third - first, second - first);

            return normal.LengthSquared() > Epsilon ? Vector3.Normalize(normal) : bsp.Planes[face.PlaneNum].Normal;
        }

        // The texture runs along the horizontal direction of the surface and down along world gravity
        private static BspOverlay CreateDecalOverlay(int index, Vector3 origin, Vector3 normal, Vector2 size)
        {
            var right = Vector3.Cross(Vector3.UnitZ, normal);

            if (right.LengthSquared() < 1e-4f)
            {
                right = Vector3.UnitX;
            }

            right = Vector3.Normalize(right);

            var halfWidth = size.X * 0.5f;
            var halfHeight = size.Y * 0.5f;

            return new BspOverlay
            {
                Index = index,
                Id = -1,
                RenderOrder = 1,
                U = new Vector2(0f, 1f),
                V = new Vector2(0f, 1f),
                Corners =
                [
                    new Vector2(-halfWidth, -halfHeight),
                    new Vector2(-halfWidth, halfHeight),
                    new Vector2(halfWidth, halfHeight),
                    new Vector2(halfWidth, -halfHeight),
                ],
                Origin = origin,
                BasisU = right,
                BasisV = Vector3.Cross(right, normal),
                BasisNormal = normal,
            };
        }

        private static BspMaterialMesh CreateTarget(BspOverlay overlay, BspTextureInfo texInfo, string material)
        {
            var flags = BspMeshFlags.Overlay | GetMaterialFlags(texInfo, material);

            if (overlay.IsWater)
            {
                flags |= BspMeshFlags.Water;
            }

            return new BspMaterialMesh(material)
            {
                Flags = flags,
                RenderOrder = overlay.RenderOrder,
                OverlayIndex = overlay.Index,
                FadeDistMinSq = overlay.FadeDistMinSq,
                FadeDistMaxSq = overlay.FadeDistMaxSq,
            };
        }

        private static void ClipTriangle(BspOverlay overlay, BspVertex a, BspVertex b, BspVertex c, BspMaterialMesh target,
            List<ClipVertex> polygon, List<ClipVertex> scratch)
        {
            var normal = Vector3.Normalize(overlay.BasisNormal);

            if (float.IsNaN(normal.X))
            {
                return;
            }

            Vector3 Project(Vector3 position) => position - (normal * Vector3.Dot(normal, position - overlay.Origin));

            var pa = Project(a.Position);
            var pb = Project(b.Position);
            var pc = Project(c.Position);

            var edgeAB = pb - pa;
            var edgeAC = pc - pa;
            var twiceArea = Vector3.Dot(Vector3.Cross(edgeAB, edgeAC), normal);

            if (MathF.Abs(twiceArea) < 1e-3f)
            {
                return;
            }

            polygon.Clear();

            var corners = overlay.Corners;

            for (var i = 0; i < 4; i++)
            {
                polygon.Add(new ClipVertex
                {
                    Position = overlay.Origin + (corners[i].X * overlay.BasisU) + (corners[i].Y * overlay.BasisV),
                });
            }

            var sign = twiceArea > 0f ? 1f : -1f;

            ClipByEdge(polygon, scratch, pa, pb - pa, normal, sign);
            ClipByEdge(polygon, scratch, pb, pc - pb, normal, sign);
            ClipByEdge(polygon, scratch, pc, pa - pc, normal, sign);

            if (polygon.Count < 3)
            {
                return;
            }

            var baseIndex = target.VertexList.Count;

            foreach (var clipped in polygon)
            {
                var offset = clipped.Position - pa;
                var weightC = Vector3.Dot(Vector3.Cross(edgeAB, offset), normal) / twiceArea;
                var weightB = Vector3.Dot(Vector3.Cross(offset, edgeAC), normal) / twiceArea;
                var weightA = 1f - weightB - weightC;

                var vertexNormal = Vector3.Normalize((a.Normal * weightA) + (b.Normal * weightB) + (c.Normal * weightC));
                var tangent = (new Vector3(a.Tangent.X, a.Tangent.Y, a.Tangent.Z) * weightA)
                    + (new Vector3(b.Tangent.X, b.Tangent.Y, b.Tangent.Z) * weightB)
                    + (new Vector3(c.Tangent.X, c.Tangent.Y, c.Tangent.Z) * weightC);

                tangent -= vertexNormal * Vector3.Dot(tangent, vertexNormal);
                tangent = tangent.LengthSquared() > 1e-8f ? Vector3.Normalize(tangent) : new Vector3(a.Tangent.X, a.Tangent.Y, a.Tangent.Z);

                target.VertexList.Add(new BspVertex
                {
                    Position = (a.Position * weightA) + (b.Position * weightB) + (c.Position * weightC),
                    Normal = vertexNormal,
                    TexCoord = GetOverlayTexCoord(overlay, clipped.Position),
                    LightmapCoord = (a.LightmapCoord * weightA) + (b.LightmapCoord * weightB) + (c.LightmapCoord * weightC),
                    DisplacementAlpha = 1f,
                    Tangent = new Vector4(tangent, a.Tangent.W),
                });
            }

            var triangleNormal = Vector3.Normalize(a.Normal + b.Normal + c.Normal);

            for (var i = 1; i < polygon.Count - 1; i++)
            {
                var v0 = target.VertexList[baseIndex].Position;
                var v1 = target.VertexList[baseIndex + i].Position;
                var v2 = target.VertexList[baseIndex + i + 1].Position;
                var facing = Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), triangleNormal);

                if (facing >= 0f)
                {
                    target.IndexList.Add(baseIndex);
                    target.IndexList.Add(baseIndex + i);
                    target.IndexList.Add(baseIndex + i + 1);
                }
                else
                {
                    target.IndexList.Add(baseIndex);
                    target.IndexList.Add(baseIndex + i + 1);
                    target.IndexList.Add(baseIndex + i);
                }
            }
        }

        private static void ClipByEdge(List<ClipVertex> polygon, List<ClipVertex> scratch, Vector3 edgeStart, Vector3 edge, Vector3 normal, float sign)
        {
            if (polygon.Count == 0)
            {
                return;
            }

            scratch.Clear();

            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Count];
                var currentDistance = Vector3.Dot(Vector3.Cross(edge, current.Position - edgeStart), normal) * sign;
                var nextDistance = Vector3.Dot(Vector3.Cross(edge, next.Position - edgeStart), normal) * sign;
                var currentInside = currentDistance >= -Epsilon;
                var nextInside = nextDistance >= -Epsilon;

                if (currentInside)
                {
                    scratch.Add(current);
                }

                if (currentInside != nextInside)
                {
                    var fraction = currentDistance / (currentDistance - nextDistance);

                    scratch.Add(new ClipVertex
                    {
                        Position = Vector3.Lerp(current.Position, next.Position, fraction),
                    });
                }
            }

            polygon.Clear();
            polygon.AddRange(scratch);
        }

        // The corners are not always a parallelogram, so the texture coordinate is the bilinear coordinate of the point in the quad
        private static Vector2 GetOverlayTexCoord(BspOverlay overlay, Vector3 position)
        {
            var corners = overlay.Corners;
            var offset = position - overlay.Origin;
            var point = new Vector2(Vector3.Dot(offset, overlay.BasisU), Vector3.Dot(offset, overlay.BasisV));

            // Corners 0 and 1 are at s = 0, corners 0 and 3 at t = 0
            var origin = corners[0];
            var edgeS = corners[3] - origin;
            var edgeT = corners[1] - origin;
            var twist = origin - corners[3] + corners[2] - corners[1];
            var local = point - origin;

            var k2 = Cross(twist, edgeT);
            var k1 = Cross(edgeS, edgeT) + Cross(local, twist);
            var k0 = Cross(local, edgeS);

            float s;
            float t;

            if (MathF.Abs(k2) < 1e-6f)
            {
                t = MathF.Abs(k1) < 1e-6f ? 0f : -k0 / k1;
            }
            else
            {
                var discriminant = MathF.Max(k1 * k1 - (4f * k0 * k2), 0f);
                var root = MathF.Sqrt(discriminant);
                t = (-k1 - root) / (2f * k2);

                if (t < 0f || t > 1f)
                {
                    t = (-k1 + root) / (2f * k2);
                }
            }

            var divisor = edgeS + (twist * t);
            var remainder = local - (edgeT * t);

            if (MathF.Abs(divisor.X) > MathF.Abs(divisor.Y))
            {
                s = remainder.X / divisor.X;
            }
            else
            {
                s = divisor.Y == 0f ? 0f : remainder.Y / divisor.Y;
            }

            return new Vector2(
                overlay.U.X + ((overlay.U.Y - overlay.U.X) * s),
                overlay.V.X + ((overlay.V.Y - overlay.V.X) * t));
        }

        private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);
    }
}
