using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Rebuilds a <see cref="CMapStaticOverlay"/> from its projected map geometry, this code attempts to figure out the source
    /// mesh the overlay was projected from.
    /// </summary>
    public static class HammerOverlayBuilder
    {
        /// <summary>
        /// How far above the surface the reconstructed overlay mesh should sit.
        /// </summary>
        public const float MeshHeight = 16f;

        /// <summary>
        /// How much further than its receiving surfaces an overlay projects, and how much wider than their angle
        /// to the projection it accepts faces, this gives some leeway to account for imprecision.
        /// </summary>
        public const float ProjectionMargin = 8f;

        // surface quantisation value, if normals are more than this angle, they are treated as a different "surface"
        private static readonly float SameSurfaceCos = MathF.Cos(float.DegreesToRadians(10f));

        // steepest angle at which a projection direction still counts as seeing a surface
        private static readonly float SurfaceSeenCos = MathF.Cos(float.DegreesToRadians(75f));

        // receiver triangles steeper than this against the projection are left out of the lifted mesh
        private static readonly float GrazingCos = MathF.Cos(float.DegreesToRadians(85f));

        // distance for welding overlapping vertices
        private const float WeldDistance = 1f / 64f;

        // in plane offset applied to a rebuilt overlay so its clip edges sit clear of the receiving geometry
        private const float ProjectionNudge = 1f / 128f;

        /// <summary>
        /// Rebuilds the overlays a compiled overlay mesh came from, the geometry is welded and split into connected pieces first using <see cref="PolygonMesh.RemergeDrawCalls"/>.
        /// </summary>
        public static List<CMapStaticOverlay> FromProjectedGeometry(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector2> texCoords, ReadOnlySpan<int> triangles,
            string material, Func<string, Vector2?>? textureSizeProvider = null)
        {
            var builder = new HammerMeshBuilder();
            var baseVertex = builder.AddVertices(positions);

            Span<int> indices = stackalloc int[3];
            Span<HammerMeshBuilder.Corner> corners = stackalloc HammerMeshBuilder.Corner[3];

            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                for (var i = 0; i < 3; i++)
                {
                    indices[i] = baseVertex + triangles[t + i];
                    corners[i] = new HammerMeshBuilder.Corner(TexCoord: texCoords[triangles[t + i]]);
                }

                builder.AddFace(indices, material, corners);
            }

            var overlays = new List<CMapStaticOverlay>();

            foreach (var piece in builder.Mesh.RemergeDrawCalls(WeldDistance))
            {
                var overlay = FromProjectedMesh(piece, material, textureSizeProvider);

                if (overlay is not null)
                {
                    overlays.Add(overlay);
                }
            }

            return overlays;
        }

        /// <summary>
        /// Rebuilds an overlay from a <see cref="PolygonMesh"/>.
        /// </summary>
        public static CMapStaticOverlay? FromProjectedMesh(PolygonMesh projected, string material, Func<string, Vector2?>? textureSizeProvider = null)
        {
            // one position per corner, the texture coordinates live on the corners
            var positions = new List<Vector3>();
            var texCoords = new List<Vector2>();
            var triangles = new List<int>();

            foreach (var hFace in projected.FaceHandles)
            {
                var first = positions.Count;
                var hEdge = hFace.Edge;

                do
                {
                    positions.Add(projected.Positions[hEdge.Vertex]);
                    texCoords.Add(projected.TextureCoords[hEdge]);
                    hEdge = hEdge.NextEdge;
                }
                while (hEdge != hFace.Edge);

                for (var i = first + 1; i + 1 < positions.Count; i++)
                {
                    triangles.Add(first);
                    triangles.Add(i);
                    triangles.Add(i + 1);
                }
            }

            return FromProjectedMesh(CollectionsMarshal.AsSpan(positions), CollectionsMarshal.AsSpan(texCoords), CollectionsMarshal.AsSpan(triangles), material, textureSizeProvider);
        }

        /// <summary>
        /// Rebuilds an overlay from its projected triangles, this should only be fed singular connected mesh islands.
        /// </summary>
        public static CMapStaticOverlay? FromProjectedMesh(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector2> texCoords, ReadOnlySpan<int> triangles,
            string material, Func<string, Vector2?>? textureSizeProvider = null)
        {
            var triangleCount = triangles.Length / 3;
            var normals = new Vector3[triangleCount];
            var areas = new float[triangleCount];
            var gradientsU = new Vector3[triangleCount];
            var gradientsV = new Vector3[triangleCount];
            var hasGradients = new bool[triangleCount];

            var facing = Vector3.Zero;
            var centroid = Vector3.Zero;
            var totalArea = 0f;

            for (var t = 0; t < triangleCount; t++)
            {
                var a = positions[triangles[t * 3]];
                var b = positions[triangles[t * 3 + 1]];
                var c = positions[triangles[t * 3 + 2]];

                var cross = Vector3.Cross(b - a, c - a);
                var doubleArea = cross.Length();

                if (doubleArea < 1e-4f)
                {
                    continue;
                }

                normals[t] = cross / doubleArea;
                areas[t] = doubleArea / 2f;
                facing += cross;
                centroid += (a + b + c) / 3f * areas[t];
                totalArea += areas[t];

                // computes the gradients of the texture coordinates
                var e0 = b - a;
                var e1 = c - a;
                var t0 = texCoords[triangles[t * 3 + 1]] - texCoords[triangles[t * 3]];
                var t1 = texCoords[triangles[t * 3 + 2]] - texCoords[triangles[t * 3]];
                var det = t0.X * t1.Y - t1.X * t0.Y;

                if (MathF.Abs(det) > 1e-9f)
                {
                    gradientsU[t] = (t1.Y * e0 - t0.Y * e1) / det;
                    gradientsV[t] = (-t1.X * e0 + t0.X * e1) / det;
                    hasGradients[t] = true;
                }
            }

            if (totalArea <= 0f || facing.LengthSquared() < 1e-12f)
            {
                return null;
            }

            centroid /= totalArea;

            // find the projection direction for this decal
            var direction = ChooseProjectionDirection(positions, triangles, normals, areas, gradientsU, gradientsV, hasGradients, facing, totalArea);

            // the overlay plane sits above the highest receiving point, with the origin at that level under the
            // centroid, this makes it nicer to work with in hammer
            var top = float.MinValue;
            var bottom = float.MaxValue;

            foreach (var index in triangles)
            {
                var depth = Vector3.Dot(direction, positions[index]);
                top = MathF.Max(top, depth);
                bottom = MathF.Min(bottom, depth);
            }

            var origin = centroid + direction * (top - Vector3.Dot(direction, centroid));

            // local x follows the texture's u direction where the triangles agree on one, any direction across the
            // projection otherwise, local Z is the projection direction
            //
            // a projected surface's u gradient is the overlay's own u axis plus a depth term along the projection direction, from the surface's slope, so
            // flattening the gradient against the direction strips the depth and leaves the overlay's axis
            var uDirection = Vector3.Zero;

            for (var t = 0; t < triangleCount; t++)
            {
                if (hasGradients[t])
                {
                    uDirection += areas[t] * (gradientsU[t] - direction * Vector3.Dot(gradientsU[t], direction));
                }
            }

            var xAxis = uDirection.LengthSquared() > 1e-6f ? uDirection : AnyPerpendicular(direction);
            xAxis = Vector3.Normalize(xAxis - direction * Vector3.Dot(xAxis, direction));
            var yAxis = Vector3.Cross(direction, xAxis);

            // lift the triangles onto the overlay's plane, keeping their texture coordinates
            var builder = new HammerMeshBuilder { TextureSizeProvider = textureSizeProvider };
            var localPositions = new Vector3[positions.Length];

            for (var i = 0; i < positions.Length; i++)
            {
                var offset = positions[i] - origin;
                localPositions[i] = new Vector3(Vector3.Dot(offset, xAxis), Vector3.Dot(offset, yAxis), MeshHeight);
            }

            var baseVertex = builder.AddVertices(localPositions);

            Span<int> indices = stackalloc int[3];
            Span<HammerMeshBuilder.Corner> corners = stackalloc HammerMeshBuilder.Corner[3];
            var steepestCos = 1f;

            // triangles that survive the lift, nearest the overlay plane first: on a curved or ribbed surface an
            // overhang can put geometry behind other geometry along the projection, flattening then stacks the two
            // on top of each other, and every face of the overlay mesh projects, so the decal would render twice there
            var lifted = new List<(int Triangle, float Depth)>();

            for (var t = 0; t < triangleCount; t++)
            {
                // only what faced the overlay, and still has an area once flattened along the projection, past
                // grazing the footprint squishes into slivers that only stack over the neighbouring geometry
                var facingCos = Vector3.Dot(normals[t], direction);

                if (areas[t] <= 0f || facingCos <= GrazingCos)
                {
                    continue;
                }

                var la = localPositions[triangles[t * 3]];
                var lb = localPositions[triangles[t * 3 + 1]];
                var lc = localPositions[triangles[t * 3 + 2]];
                var liftedDoubleArea = (lb.X - la.X) * (lc.Y - la.Y) - (lc.X - la.X) * (lb.Y - la.Y);

                if (liftedDoubleArea < 1e-4f)
                {
                    continue;
                }

                steepestCos = MathF.Min(steepestCos, facingCos);

                var depth = (Vector3.Dot(direction, positions[triangles[t * 3]])
                    + Vector3.Dot(direction, positions[triangles[t * 3 + 1]])
                    + Vector3.Dot(direction, positions[triangles[t * 3 + 2]])) / 3f;

                lifted.Add((t, depth));
            }

            lifted.Sort((x, y) => y.Depth.CompareTo(x.Depth));

            Span<Vector3> clippedCorners = stackalloc Vector3[3];

            foreach (var resolved in ClipOverlappingTriangles(positions, texCoords, triangles, localPositions, direction, lifted))
            {
                if (resolved.Polygon is null)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        indices[i] = baseVertex + triangles[resolved.Triangle * 3 + i];
                        corners[i] = new HammerMeshBuilder.Corner(TexCoord: texCoords[triangles[resolved.Triangle * 3 + i]], Normal: Vector3.UnitZ);
                    }

                    builder.AddFace(indices, material, corners);
                    continue;
                }

                // what remains of a clipped triangle, fanned into triangles on fresh vertices, welding reconnects them
                for (var i = 1; i + 1 < resolved.Polygon.Length; i++)
                {
                    clippedCorners[0] = new Vector3(resolved.Polygon[0], MeshHeight);
                    clippedCorners[1] = new Vector3(resolved.Polygon[i], MeshHeight);
                    clippedCorners[2] = new Vector3(resolved.Polygon[i + 1], MeshHeight);

                    var clippedVertex = builder.AddVertices(clippedCorners);

                    indices[0] = clippedVertex;
                    indices[1] = clippedVertex + 1;
                    indices[2] = clippedVertex + 2;
                    corners[0] = new HammerMeshBuilder.Corner(TexCoord: resolved.PolygonTexCoords![0], Normal: Vector3.UnitZ);
                    corners[1] = new HammerMeshBuilder.Corner(TexCoord: resolved.PolygonTexCoords[i], Normal: Vector3.UnitZ);
                    corners[2] = new HammerMeshBuilder.Corner(TexCoord: resolved.PolygonTexCoords[i + 1], Normal: Vector3.UnitZ);

                    builder.AddFace(indices, material, corners);
                }
            }

            if (!builder.Mesh.FaceHandles.Any())
            {
                return null;
            }

            // weld the lifted triangles together, but keep them as triangles: combining them into polygons can leave
            // illegal overlapping edges and distorts how the decal renders
            builder.Mesh.MergeCoincidentOpenEdges(WeldDistance);
            builder.Mesh.MergeVerticesWithinDistance(WeldDistance);

            // slivers collapse entirely once their vertices on straight runs go
            if (!builder.Mesh.FaceHandles.Any())
            {
                return null;
            }

            var rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                xAxis.X, xAxis.Y, xAxis.Z, 0f,
                yAxis.X, yAxis.Y, yAxis.Z, 0f,
                direction.X, direction.Y, direction.Z, 0f,
                0f, 0f, 0f, 1f));

            // the rebuilt overlay is exact: every clip edge of its mesh sits precisely on vertices of the receiving
            // geometry it was lifted from, which is the degenerate case of hammers projection clipper and shows up
            // as striped projections, receiver triangles falling in or out on float noise. a tiny in plane offset
            // keeps every clip plane clear of the receivers, and moves the decal imperceptibly
            var nudge = (xAxis + yAxis) * ProjectionNudge;

            return new CMapStaticOverlay
            {
                MeshData = builder.GenerateMesh(),
                Origin = origin + nudge,
                Angles = EntityTransformHelper.ToEulerAngles(rotation),
                ProjectionFar = MathF.Ceiling(MeshHeight + (top - bottom) + ProjectionMargin),
                BackFacingAngle = MathF.Min(90f, MathF.Ceiling(float.RadiansToDegrees(MathF.Acos(Math.Clamp(steepestCos, -1f, 1f))) + ProjectionMargin)),
            };
        }

        // uses multiple techniques to try to find a reasonable direction that this overlay face could have been projected on
        private static Vector3 ChooseProjectionDirection(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> triangles, Vector3[] normals, float[] areas,
            Vector3[] gradientsU, Vector3[] gradientsV, bool[] hasGradients, Vector3 facing, float totalArea)
        {
            // quantised surfaces
            var surfaces = FindSurfaces(normals, areas);

            // ignore tiny surfaces the decal might have landed on, like sliver triangles on the edge on a complex models, they
            // make decision making too noise
            var significantSurfaces = surfaces.Where(p => p.Area >= 0.15f * totalArea).Select(p => p.Normal).ToList();

            // areaweighted average of all the triangle normals
            var facingDirection = Vector3.Normalize(facing);

            // whatever direction wins must see every one of those surfaces, a projection cannot apply to a surface it runs parallel to, or away from
            bool SeesAllSurfaces(Vector3 candidate) => significantSurfaces.All(n => Vector3.Dot(n, candidate) > SurfaceSeenCos);

            // for simple quad overlays, that is overlays that were not edited using hammer's "Toggle Overlay Shapes" option, this can
            // find the exact projection direction using surface UVs, but it will fail for any complex overlay made from a complex mesh
            var estimated = EstimateProjectionDirection(positions, triangles, normals, areas, gradientsU, gradientsV, hasGradients);

            // the estimate is a line without a side, the projector sits on the side the surfaces face
            if (estimated is { } estimatedDirection && Vector3.Dot(estimatedDirection, facing) < 0f)
            {
                estimated = -estimatedDirection;
            }

            if (estimated is { } validEstimate && SeesAllSurfaces(validEstimate))
            {
                return validEstimate;
            }

            // failed to find a good reconstruction, there is more than one significant surface AND we see all surfaces, good enough
            // this might be a complex decal so just use the average facing direction here
            if (significantSurfaces.Count > 1 && SeesAllSurfaces(facingDirection))
            {
                return facingDirection;
            }

            // if we got this far, it means we are either:
            //
            // - one "flat" surface, which is good, but we still want to pick the best fitting direction cuz surfaces are never really perfectly flat
            //
            // - or opposite facing surfaces no single direction can satisfy, this is a worst case scenario but due to the nature of overlay projection, this is
            //   very rarely the case
            //
            // pick the normal which sees most of the decal
            var direction = facingDirection;
            var bestScore = float.MinValue;

            foreach (var (candidate, _) in surfaces)
            {
                var score = 0f;

                for (var t = 0; t < normals.Length; t++)
                {
                    score += areas[t] * MathF.Max(0f, Vector3.Dot(normals[t], candidate));
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    direction = candidate;
                }
            }

            return direction;
        }

        // tries to reconstruct and undo the projection of the overlay mesh using UV gradients,
        // the texture lands stretched a little differently on each surface it hits depending on the surface's angle,
        // comparing the gradients of neighbouring triangles on two different surfaces cancels out what they share and the difference left over
        // points along the projection direction itself, every such pair gives one pointer

        // returns the direction when all the pointers agree on a single line, null when there is nothing to compare
        // (all one surface) or the pointers disagree (multi face overlays map each face differently),
        // the caller then falls back to the surface normals
        private static Vector3? EstimateProjectionDirection(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> triangles, Vector3[] normals, float[] areas,
            Vector3[] gradientsU, Vector3[] gradientsV, bool[] hasGradients)
        {
            // need to rebuild triangle adjacency by position
            var pointIds = new Dictionary<(int X, int Y, int Z), int>();
            var vertexPoint = new int[positions.Length];

            for (var i = 0; i < positions.Length; i++)
            {
                var cell = ((int)MathF.Round(positions[i].X / WeldDistance), (int)MathF.Round(positions[i].Y / WeldDistance), (int)MathF.Round(positions[i].Z / WeldDistance));

                if (!pointIds.TryGetValue(cell, out var id))
                {
                    id = pointIds.Count;
                    pointIds.Add(cell, id);
                }

                vertexPoint[i] = id;
            }

            // neighbouring triangles meet at a point, an edge would miss most pairs across a corner where the two
            // surfaces were triangulated independently
            var pointTriangles = new Dictionary<int, List<int>>();

            for (var t = 0; t < triangles.Length / 3; t++)
            {
                if (areas[t] <= 0f || !hasGradients[t])
                {
                    continue;
                }

                for (var i = 0; i < 3; i++)
                {
                    var point = vertexPoint[triangles[t * 3 + i]];

                    if (!pointTriangles.TryGetValue(point, out var list))
                    {
                        list = [];
                        pointTriangles.Add(point, list);
                    }

                    if (!list.Contains(t))
                    {
                        list.Add(t);
                    }
                }
            }

            // every pointer is folded into a covariance matrix as its outer product, the standard setup for finding
            // the best fit line through direction samples (principal component analysis): a pointer and its negation
            // count the same, so it doesn't matter which way along the projection each one happens to point, and
            // longer pointers (sharper corners) weigh themselves in more. evidence collects the matrix trace,
            // the total energy of all pointers in all directions
            var covariance = new float[3, 3];
            var evidence = 0f;

            void Accumulate(Vector3 difference)
            {
                covariance[0, 0] += difference.X * difference.X;
                covariance[0, 1] += difference.X * difference.Y;
                covariance[0, 2] += difference.X * difference.Z;
                covariance[1, 1] += difference.Y * difference.Y;
                covariance[1, 2] += difference.Y * difference.Z;
                covariance[2, 2] += difference.Z * difference.Z;
                evidence += difference.LengthSquared();
            }

            foreach (var list in pointTriangles.Values)
            {
                for (var a = 0; a < list.Count; a++)
                {
                    for (var b = a + 1; b < list.Count; b++)
                    {
                        var t = list[a];
                        var other = list[b];

                        // only pairs on genuinely different surfaces carry information, within one surface the
                        // gradients match and their difference is just noise
                        if (Vector3.Dot(normals[t], normals[other]) < SameSurfaceCos)
                        {
                            Accumulate(gradientsU[t] - gradientsU[other]);
                            Accumulate(gradientsV[t] - gradientsV[other]);
                        }
                    }
                }
            }

            // effectively nothing to compare
            if (evidence < 1f)
            {
                return null;
            }

            covariance[1, 0] = covariance[0, 1];
            covariance[2, 0] = covariance[0, 2];
            covariance[2, 1] = covariance[1, 2];

            // the line the pointers agree on is the covariance matrix's largest eigenvector, found by power
            // iteration: multiplying a vector by the matrix over and over scales each of its components by the
            // matching eigenvalue, so the largest one takes over. the seed is nudged asymmetric so it cannot start
            // exactly perpendicular to the answer, which would keep it stuck
            var direction = Vector3.Normalize(new Vector3(covariance[0, 0] + 1e-3f, covariance[1, 1] + 2e-3f, covariance[2, 2] + 3e-3f));

            for (var iteration = 0; iteration < 64; iteration++)
            {
                var next = new Vector3(
                    covariance[0, 0] * direction.X + covariance[0, 1] * direction.Y + covariance[0, 2] * direction.Z,
                    covariance[1, 0] * direction.X + covariance[1, 1] * direction.Y + covariance[1, 2] * direction.Z,
                    covariance[2, 0] * direction.X + covariance[2, 1] * direction.Y + covariance[2, 2] * direction.Z);

                if (next.LengthSquared() < 1e-12f)
                {
                    return null;
                }

                direction = Vector3.Normalize(next);
            }

            // the found line must hold nearly all of the energy before it is trusted: principal is the energy along
            // it (the rayleigh quotient), evidence is the total in all directions (the trace), a real projection
            // puts everything on one line while unrelated mappings scatter it everywhere
            var principal =
                direction.X * (covariance[0, 0] * direction.X + covariance[0, 1] * direction.Y + covariance[0, 2] * direction.Z) +
                direction.Y * (covariance[1, 0] * direction.X + covariance[1, 1] * direction.Y + covariance[1, 2] * direction.Z) +
                direction.Z * (covariance[2, 0] * direction.X + covariance[2, 1] * direction.Y + covariance[2, 2] * direction.Z);

            return principal > 0.8f * evidence ? direction : null;
        }

        // loop the triangle normals and greedy cluster on orientation, look for an existing cluster whose normal is within SameSurfaceCos, if found
        // add the triangle's area to that cluster, otherwise start a new cluster
        private static List<(Vector3 Normal, float Area)> FindSurfaces(Vector3[] normals, float[] areas)
        {
            var surfaces = new List<(Vector3 Normal, float Area)>();

            for (var t = 0; t < normals.Length; t++)
            {
                // skip degenerate triangles
                if (areas[t] <= 0f)
                {
                    continue;
                }

                var found = false;

                for (var i = 0; i < surfaces.Count; i++)
                {
                    if (Vector3.Dot(surfaces[i].Normal, normals[t]) > SameSurfaceCos)
                    {
                        surfaces[i] = (surfaces[i].Normal, surfaces[i].Area + areas[t]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    surfaces.Add((normals[t], areas[t]));
                }
            }

            return surfaces;
        }

        private static Vector3 AnyPerpendicular(Vector3 direction)
        {
            var axis = MathF.Abs(direction.X) < MathF.Abs(direction.Y) ? Vector3.UnitX : Vector3.UnitY;
            return Vector3.Cross(direction, axis);
        }

        // normals within this angle of each other lie flat against each other
        private static readonly float SameSheetCos = MathF.Cos(float.DegreesToRadians(2f));

        // coplanar within this distance is the same sheet, further apart is a surface stacked behind another,
        // up to the height the overlay mesh sits at, which is as deep as its projection reliably reaches
        private const float SheetThickness = 1f / 16f;
        private const float MaxStackGap = MeshHeight;

        // how closely texture coordinates must agree for two copies to count as the same decal, decal textures
        // are mapped roughly once over the overlay, so this is a few texels on a typical texture
        private const float StackedUvTolerance = 1f / 256f;

        /// <summary>
        /// Flags projected triangles that duplicate the same decal region on a surface stacked behind another.
        /// The compiler clips a decal against every surface its projection reaches, so a surface hidden behind
        /// another carries a second copy that would come back as duplicated faces stacked on the rebuilt overlay.
        /// Returns one flag per triangle, set when the triangle is such a duplicate and should be left out.
        /// </summary>
        public static bool[] RemoveStackedDuplicates(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector2> texCoords, ReadOnlySpan<int> triangles)
        {
            var triangleCount = triangles.Length / 3;

            // cluster the triangles into flat sheets, duplicated copies live on parallel sheets a small gap apart
            var sheets = new List<(Vector3 Normal, float Offset, List<int> Triangles, float Area)>();

            for (var t = 0; t < triangleCount; t++)
            {
                var a = positions[triangles[t * 3]];
                var b = positions[triangles[t * 3 + 1]];
                var c = positions[triangles[t * 3 + 2]];

                var cross = Vector3.Cross(b - a, c - a);
                var doubleArea = cross.Length();

                if (doubleArea < 1e-6f)
                {
                    continue;
                }

                var normal = cross / doubleArea;
                var offset = Vector3.Dot(normal, a);
                var found = false;

                for (var i = 0; i < sheets.Count; i++)
                {
                    if (Vector3.Dot(sheets[i].Normal, normal) > SameSheetCos && MathF.Abs(sheets[i].Offset - offset) < SheetThickness)
                    {
                        sheets[i].Triangles.Add(t);
                        sheets[i] = (sheets[i].Normal, sheets[i].Offset, sheets[i].Triangles, sheets[i].Area + doubleArea / 2);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    sheets.Add((normal, offset, [t], doubleArea / 2));
                }
            }

            var dropped = new bool[triangleCount];

            // an exact copy on the same sheet is the same duplication with no gap at all
            var seen = new Dictionary<(long, long, long), int>();
            var cornerKeys = new long[3];

            foreach (var sheet in sheets)
            {
                seen.Clear();

                foreach (var t in sheet.Triangles)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var p = positions[triangles[t * 3 + i]];
                        cornerKeys[i] = ((long)MathF.Round(p.X * 64) << 42) ^ ((long)MathF.Round(p.Y * 64) << 21) ^ (long)MathF.Round(p.Z * 64);
                    }

                    Array.Sort(cornerKeys);
                    var key = (cornerKeys[0], cornerKeys[1], cornerKeys[2]);

                    if (seen.TryGetValue(key, out var first))
                    {
                        var coord = (texCoords[triangles[t * 3]] + texCoords[triangles[t * 3 + 1]] + texCoords[triangles[t * 3 + 2]]) / 3;
                        var firstCoord = (texCoords[triangles[first * 3]] + texCoords[triangles[first * 3 + 1]] + texCoords[triangles[first * 3 + 2]]) / 3;

                        if (Vector2.Distance(coord, firstCoord) < StackedUvTolerance)
                        {
                            dropped[t] = true;
                        }
                    }
                    else
                    {
                        seen[key] = t;
                    }
                }
            }

            // larger sheets win, a smaller sheet loses the triangles the larger one already covers
            sheets.Sort((x, y) => y.Area.CompareTo(x.Area));

            Span<Vector2> samples = stackalloc Vector2[10];

            // partly covered copies of the covering decal, they poke past an edge of the surface in front
            var partials = new List<int>();

            for (var k = 0; k < sheets.Count; k++)
            {
                var cover = sheets[k];
                Vector2[]? cover2d = null;
                Vector2[]? coverMin = null;
                Vector2[]? coverMax = null;

                // 2d frame on the covering sheet to compare footprints in
                var xAxis = Vector3.Normalize(AnyPerpendicular(cover.Normal));
                var yAxis = Vector3.Cross(cover.Normal, xAxis);

                for (var s = k + 1; s < sheets.Count; s++)
                {
                    var behind = sheets[s];
                    var gap = MathF.Abs(cover.Offset - behind.Offset);

                    if (Vector3.Dot(cover.Normal, behind.Normal) <= SameSheetCos || gap <= SheetThickness || gap > MaxStackGap)
                    {
                        continue;
                    }

                    if (cover2d is null)
                    {
                        // project the covering triangles once, they are checked against every stacked sheet
                        cover2d = new Vector2[cover.Triangles.Count * 3];
                        coverMin = new Vector2[cover.Triangles.Count];
                        coverMax = new Vector2[cover.Triangles.Count];

                        for (var i = 0; i < cover.Triangles.Count; i++)
                        {
                            for (var j = 0; j < 3; j++)
                            {
                                var p = positions[triangles[cover.Triangles[i] * 3 + j]];
                                cover2d[i * 3 + j] = new Vector2(Vector3.Dot(p, xAxis), Vector3.Dot(p, yAxis));
                            }

                            coverMin[i] = Vector2.Min(cover2d[i * 3], Vector2.Min(cover2d[i * 3 + 1], cover2d[i * 3 + 2]));
                            coverMax[i] = Vector2.Max(cover2d[i * 3], Vector2.Max(cover2d[i * 3 + 1], cover2d[i * 3 + 2]));
                        }
                    }

                    foreach (var t in behind.Triangles)
                    {
                        if (dropped[t])
                        {
                            continue;
                        }

                        var a2 = Project(positions[triangles[t * 3]], xAxis, yAxis);
                        var b2 = Project(positions[triangles[t * 3 + 1]], xAxis, yAxis);
                        var c2 = Project(positions[triangles[t * 3 + 2]], xAxis, yAxis);
                        var centre = (a2 + b2 + c2) / 3;

                        // corners, edge midpoints, the centre and points partway in, enough to tell
                        // a covered triangle from one that only brushes the covering sheet
                        samples[0] = a2;
                        samples[1] = b2;
                        samples[2] = c2;
                        samples[3] = (a2 + b2) / 2;
                        samples[4] = (b2 + c2) / 2;
                        samples[5] = (c2 + a2) / 2;
                        samples[6] = centre;
                        samples[7] = (a2 + centre) / 2;
                        samples[8] = (b2 + centre) / 2;
                        samples[9] = (c2 + centre) / 2;

                        var centreCoord = (texCoords[triangles[t * 3]] + texCoords[triangles[t * 3 + 1]] + texCoords[triangles[t * 3 + 2]]) / 3;

                        var inside = 0;
                        var sameDecal = false;

                        for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                        {
                            var sample = samples[sampleIndex];

                            for (var i = 0; i < cover.Triangles.Count; i++)
                            {
                                if (dropped[cover.Triangles[i]]
                                    || sample.X < coverMin![i].X || sample.Y < coverMin[i].Y
                                    || sample.X > coverMax![i].X || sample.Y > coverMax[i].Y)
                                {
                                    continue;
                                }

                                var weights = BarycentricWeights(cover2d.AsSpan(i * 3, 3), sample);

                                if (weights is not { } w)
                                {
                                    continue;
                                }

                                inside++;

                                // check the copies sample the decal the same way at the centre, overlapping
                                // decals that are genuinely different keep both their meshes
                                if (sampleIndex == 6)
                                {
                                    var coverTriangle = cover.Triangles[i];
                                    var coverCoord =
                                        texCoords[triangles[coverTriangle * 3]] * w.X +
                                        texCoords[triangles[coverTriangle * 3 + 1]] * w.Y +
                                        texCoords[triangles[coverTriangle * 3 + 2]] * w.Z;

                                    sameDecal = Vector2.Distance(coverCoord, centreCoord) < StackedUvTolerance;
                                }

                                break;
                            }
                        }

                        // mostly covered is still a duplicate, a copy can poke slightly past an edge of the
                        // surface in front, the sliver it adds is not worth the stacked faces it brings along
                        if (sameDecal && inside >= 8)
                        {
                            dropped[t] = true;
                        }
                        else if (sameDecal && inside >= 4)
                        {
                            partials.Add(t);
                        }
                    }
                }
            }

            // the edge triangles of a dropped copy fail the coverage check where the copy pokes past the surface
            // in front, they still belong to the copy: pull in partly covered triangles touching a dropped one
            if (partials.Count > 0)
            {
                var droppedCorners = new HashSet<(long, long, long)>();

                for (var t = 0; t < triangleCount; t++)
                {
                    if (dropped[t])
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            droppedCorners.Add(CornerKey(positions[triangles[t * 3 + i]]));
                        }
                    }
                }

                var spread = true;

                while (spread)
                {
                    spread = false;

                    foreach (var t in partials)
                    {
                        if (dropped[t])
                        {
                            continue;
                        }

                        for (var i = 0; i < 3; i++)
                        {
                            if (droppedCorners.Contains(CornerKey(positions[triangles[t * 3 + i]])))
                            {
                                dropped[t] = true;
                                spread = true;

                                for (var j = 0; j < 3; j++)
                                {
                                    droppedCorners.Add(CornerKey(positions[triangles[t * 3 + j]]));
                                }

                                break;
                            }
                        }
                    }
                }
            }

            return dropped;
        }

        private static Vector2 Project(Vector3 position, Vector3 xAxis, Vector3 yAxis)
            => new(Vector3.Dot(position, xAxis), Vector3.Dot(position, yAxis));

        // how much nearer the overlay a triangle must sit before it hides one behind it, this keeps flat
        // neighbours that merely share an edge from hiding each other
        private const float OverhangDepthMargin = 1f / 128f;

        // overlaps and leftovers smaller than this are noise, not worth a face
        private const float MinOverlapArea = 1f / 512f;

        // a lifted triangle after overlap resolution: untouched when Polygon is null, otherwise clipped down to
        // the polygon, with a texture coordinate per polygon corner
        private readonly record struct ResolvedTriangle(int Triangle, Vector2[]? Polygon, Vector2[]? PolygonTexCoords);

        // walks the lifted triangles nearest the overlay first and clips away whatever is already covered: on a
        // curved, ribbed or seamed receiver the projection can put two copies of the same decal region on top of
        // each other, and every face of the overlay mesh projects, so the decal would render twice there, the
        // covered region is redundant anyway because the overlay projects through geometry
        private static List<ResolvedTriangle> ClipOverlappingTriangles(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector2> texCoords, ReadOnlySpan<int> triangles,
            Vector3[] localPositions, Vector3 direction, List<(int Triangle, float Depth)> lifted)
        {
            var resolved = new List<ResolvedTriangle>(lifted.Count);

            // every kept piece interpolates depth and texture coordinates from the source triangle it was cut
            // from, copied per corner up front so the helpers below have plain arrays to work over
            var corner2d = new Vector2[triangles.Length];
            var cornerDepths = new float[triangles.Length];
            var cornerCoords = new Vector2[triangles.Length];

            for (var i = 0; i < triangles.Length; i++)
            {
                corner2d[i] = new Vector2(localPositions[triangles[i]].X, localPositions[triangles[i]].Y);
                cornerDepths[i] = Vector3.Dot(direction, positions[triangles[i]]);
                cornerCoords[i] = texCoords[triangles[i]];
            }

            var kept = new List<(Vector2 A, Vector2 B, Vector2 C, int Source, Vector2 Min, Vector2 Max)>();

            Vector3 WeightsOf(int t, Vector2 point)
                => UnclampedBarycentricWeights(corner2d.AsSpan(t * 3, 3), point);

            float DepthAt(int t, Vector2 point)
            {
                var w = WeightsOf(t, point);
                return cornerDepths[t * 3] * w.X + cornerDepths[t * 3 + 1] * w.Y + cornerDepths[t * 3 + 2] * w.Z;
            }

            Vector2 TexCoordAt(int t, Vector2 point)
            {
                var w = WeightsOf(t, point);
                return cornerCoords[t * 3] * w.X + cornerCoords[t * 3 + 1] * w.Y + cornerCoords[t * 3 + 2] * w.Z;
            }

            var polygon = new List<Vector2>();
            var clippedPolygon = new List<Vector2>();
            var overlap = new List<Vector2>();

            foreach (var (t, _) in lifted)
            {
                polygon.Clear();
                polygon.Add(corner2d[t * 3]);
                polygon.Add(corner2d[t * 3 + 1]);
                polygon.Add(corner2d[t * 3 + 2]);
                var clipped = false;

                for (var k = 0; k < kept.Count && polygon.Count >= 3; k++)
                {
                    var cover = kept[k];
                    var inBounds = false;

                    foreach (var point in polygon)
                    {
                        if (point.X >= cover.Min.X && point.Y >= cover.Min.Y && point.X <= cover.Max.X && point.Y <= cover.Max.Y)
                        {
                            inBounds = true;
                            break;
                        }
                    }

                    if (!inBounds)
                    {
                        continue;
                    }

                    // clip a few times: cutting along one covering edge may leave overlap past another
                    for (var pass = 0; pass < 3 && polygon.Count >= 3; pass++)
                    {
                        overlap.Clear();
                        overlap.AddRange(polygon);
                        ClipInside(overlap, clippedPolygon, cover.A, cover.B);
                        ClipInside(overlap, clippedPolygon, cover.B, cover.C);
                        ClipInside(overlap, clippedPolygon, cover.C, cover.A);

                        if (PolygonArea(overlap) < MinOverlapArea)
                        {
                            break;
                        }

                        if (pass == 0)
                        {
                            // the cover hides the overlap when it is clearly nearer the overlay, or level with it and
                            // mapping the same decal region there, overlapping decals that are genuinely different
                            // keep both their meshes
                            var centre = PolygonCentre(overlap);
                            var depth = DepthAt(t, centre);
                            var coverDepth = DepthAt(cover.Source, centre);

                            var hides = coverDepth > depth + OverhangDepthMargin
                                || (MathF.Abs(coverDepth - depth) <= OverhangDepthMargin
                                    && Vector2.Distance(TexCoordAt(cover.Source, centre), TexCoordAt(t, centre)) < StackedUvTolerance);

                            if (!hides)
                            {
                                break;
                            }
                        }

                        // cut along the covering edge that keeps the most of the triangle
                        var bestArea = -1f;
                        var bestEdge = 0;

                        for (var edge = 0; edge < 3; edge++)
                        {
                            var (p0, p1) = CoverEdge(cover, edge);
                            overlap.Clear();
                            overlap.AddRange(polygon);
                            ClipOutside(overlap, clippedPolygon, p0, p1);
                            var area = PolygonArea(overlap);

                            if (area > bestArea)
                            {
                                bestArea = area;
                                bestEdge = edge;
                            }
                        }

                        clipped = true;

                        if (bestArea < MinOverlapArea)
                        {
                            polygon.Clear();
                        }
                        else
                        {
                            var (p0, p1) = CoverEdge(cover, bestEdge);
                            ClipOutside(polygon, clippedPolygon, p0, p1);
                        }
                    }
                }

                if (polygon.Count < 3 || PolygonArea(polygon) < MinOverlapArea)
                {
                    continue;
                }

                if (clipped)
                {
                    var corners = polygon.ToArray();
                    var coords = new Vector2[corners.Length];

                    for (var i = 0; i < corners.Length; i++)
                    {
                        coords[i] = TexCoordAt(t, corners[i]);
                    }

                    resolved.Add(new ResolvedTriangle(t, corners, coords));
                }
                else
                {
                    resolved.Add(new ResolvedTriangle(t, null, null));
                }

                // fan what was kept into covering pieces for the triangles behind to be clipped against
                for (var i = 1; i + 1 < polygon.Count; i++)
                {
                    var a = polygon[0];
                    var b = polygon[i];
                    var c = polygon[i + 1];
                    kept.Add((a, b, c, t, Vector2.Min(a, Vector2.Min(b, c)), Vector2.Max(a, Vector2.Max(b, c))));
                }
            }

            return resolved;
        }

        private static (Vector2, Vector2) CoverEdge((Vector2 A, Vector2 B, Vector2 C, int Source, Vector2 Min, Vector2 Max) cover, int edge)
            => edge switch
            {
                0 => (cover.A, cover.B),
                1 => (cover.B, cover.C),
                _ => (cover.C, cover.A),
            };

        // clips the polygon to one side of the directed line through p0 and p1, inside is the left of a counter
        // clockwise triangle's edge
        private static void ClipInside(List<Vector2> polygon, List<Vector2> scratch, Vector2 p0, Vector2 p1)
            => ClipToLine(polygon, scratch, p0, p1, keepInside: true);

        private static void ClipOutside(List<Vector2> polygon, List<Vector2> scratch, Vector2 p0, Vector2 p1)
            => ClipToLine(polygon, scratch, p0, p1, keepInside: false);

        private static void ClipToLine(List<Vector2> polygon, List<Vector2> scratch, Vector2 p0, Vector2 p1, bool keepInside)
        {
            scratch.Clear();
            var edge = p1 - p0;

            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Count];
                var currentSide = edge.X * (current.Y - p0.Y) - edge.Y * (current.X - p0.X);
                var nextSide = edge.X * (next.Y - p0.Y) - edge.Y * (next.X - p0.X);
                var currentKept = keepInside ? currentSide >= 0f : currentSide <= 0f;
                var nextKept = keepInside ? nextSide >= 0f : nextSide <= 0f;

                if (currentKept)
                {
                    scratch.Add(current);
                }

                if (currentKept != nextKept && MathF.Abs(nextSide - currentSide) > 1e-12f)
                {
                    scratch.Add(current + (next - current) * (currentSide / (currentSide - nextSide)));
                }
            }

            polygon.Clear();
            polygon.AddRange(scratch);
        }

        private static float PolygonArea(List<Vector2> polygon)
        {
            var doubleArea = 0f;

            for (var i = 1; i + 1 < polygon.Count; i++)
            {
                var u = polygon[i] - polygon[0];
                var v = polygon[i + 1] - polygon[0];
                doubleArea += u.X * v.Y - u.Y * v.X;
            }

            return MathF.Abs(doubleArea) / 2f;
        }

        private static Vector2 PolygonCentre(List<Vector2> polygon)
        {
            var centre = Vector2.Zero;

            foreach (var point in polygon)
            {
                centre += point;
            }

            return centre / polygon.Count;
        }

        // barycentric weights without an inside check, for interpolating a triangle's attributes at any point
        private static Vector3 UnclampedBarycentricWeights(ReadOnlySpan<Vector2> corners, Vector2 point)
        {
            var doubleArea = (corners[1].X - corners[0].X) * (corners[2].Y - corners[0].Y) - (corners[2].X - corners[0].X) * (corners[1].Y - corners[0].Y);

            if (MathF.Abs(doubleArea) < 1e-8f)
            {
                return new Vector3(1f, 0f, 0f);
            }

            var u = ((corners[1].X - point.X) * (corners[2].Y - point.Y) - (corners[2].X - point.X) * (corners[1].Y - point.Y)) / doubleArea;
            var v = ((corners[2].X - point.X) * (corners[0].Y - point.Y) - (corners[0].X - point.X) * (corners[2].Y - point.Y)) / doubleArea;

            return new Vector3(u, v, 1f - u - v);
        }

        private static (long, long, long) CornerKey(Vector3 position)
            => ((long)MathF.Round(position.X * 64), (long)MathF.Round(position.Y * 64), (long)MathF.Round(position.Z * 64));

        // barycentric weights of a point against a 2d triangle, null when the point lies outside it
        private static Vector3? BarycentricWeights(ReadOnlySpan<Vector2> corners, Vector2 point)
        {
            var doubleArea = (corners[1].X - corners[0].X) * (corners[2].Y - corners[0].Y) - (corners[2].X - corners[0].X) * (corners[1].Y - corners[0].Y);

            if (MathF.Abs(doubleArea) < 1e-8f)
            {
                return null;
            }

            var u = ((corners[1].X - point.X) * (corners[2].Y - point.Y) - (corners[2].X - point.X) * (corners[1].Y - point.Y)) / doubleArea;
            var v = ((corners[2].X - point.X) * (corners[0].Y - point.Y) - (corners[0].X - point.X) * (corners[2].Y - point.Y)) / doubleArea;
            var w = 1f - u - v;

            // a little slack so shared clip edges still count as inside
            const float Epsilon = 1e-3f;

            if (u < -Epsilon || v < -Epsilon || w < -Epsilon)
            {
                return null;
            }

            return new Vector3(u, v, w);
        }
    }
}
