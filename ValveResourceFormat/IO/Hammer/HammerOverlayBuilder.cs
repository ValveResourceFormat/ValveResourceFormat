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

        // distance for welding overlapping vertices
        private const float WeldDistance = 1f / 64f;

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

            for (var t = 0; t < triangleCount; t++)
            {
                // only what faced the overlay, and still has an area once flattened along the projection
                var facingCos = Vector3.Dot(normals[t], direction);

                if (areas[t] <= 0f || facingCos <= 0.01f)
                {
                    continue;
                }

                steepestCos = MathF.Min(steepestCos, facingCos);

                var la = localPositions[triangles[t * 3]];
                var lb = localPositions[triangles[t * 3 + 1]];
                var lc = localPositions[triangles[t * 3 + 2]];
                var liftedDoubleArea = (lb.X - la.X) * (lc.Y - la.Y) - (lc.X - la.X) * (lb.Y - la.Y);

                if (liftedDoubleArea < 1e-4f)
                {
                    continue;
                }

                for (var i = 0; i < 3; i++)
                {
                    indices[i] = baseVertex + triangles[t * 3 + i];
                    corners[i] = new HammerMeshBuilder.Corner(TexCoord: texCoords[triangles[t * 3 + i]], Normal: Vector3.UnitZ);
                }

                builder.AddFace(indices, material, corners);
            }

            if (!builder.Mesh.FaceHandles.Any())
            {
                return null;
            }

            builder.Mesh.MergeCoincidentOpenEdges(WeldDistance);
            builder.Mesh.MergeVerticesWithinDistance(WeldDistance);
            builder.Mesh.CombineFacesWithMatchingTextureCoordinates();

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

            return new CMapStaticOverlay
            {
                MeshData = builder.GenerateMesh(),
                Origin = origin,
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
            bool SeesAllSurfaces(Vector3 candidate) => significantSurfaces.All(n => Vector3.Dot(n, candidate) > 0.05f);

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
    }
}
