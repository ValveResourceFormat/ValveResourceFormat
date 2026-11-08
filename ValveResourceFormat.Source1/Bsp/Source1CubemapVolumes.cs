using System.Globalization;
using System.Linq;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Fits a box to the room of each cube map sample of a map. The walls come from rays traced from the sample, the faces
/// that were lit by the sample in the game extend the box where the room is open, and boxes inside other boxes get a
/// higher priority instead of being cut.
/// </summary>
internal static class Source1CubemapVolumes
{
    // The tuning values are choices of this fit, not values of the engines
    private const int RayCount = 384;

    // A ray leans towards an axis when its direction is within about 37 degrees of it
    private const float AxisConeCosine = 0.8f;

    // The share of the rays of a side that end at or before the wall, so a doorway or a pillar does not set the side
    private const float WallPercentile = 0.75f;
    private const float MinHalfExtent = 64f;
    private const float MaxOpenHalfExtent = 2048f;

    // How far an open side reaches towards the nearest other sample, as a share of the distance to it
    private const float NeighbourReach = 0.5f;
    private const float FaceNormalOffset = 2f;
    private const float EdgeFadeShare = 0.25f;
    private const float MinEdgeFade = 16f;
    private const float Tolerance = 1f;

    /// <summary>The priority of a volume that no other volume contains.</summary>
    public const int OutermostPriority = -1;

    /// <summary>The highest priority a volume can have.</summary>
    public const int InnermostPriority = 2;

    /// <summary>The priority of the volume that covers the world, below every fitted one.</summary>
    public const int FallbackPriority = -2;

    /// <summary>The box of a cube map sample.</summary>
    internal sealed class Volume
    {
        public required Vector3 Origin { get; init; }

        /// <summary>The corner of the box with the lowest coordinates, in the world.</summary>
        public Vector3 Mins { get; set; }

        /// <summary>The corner of the box with the highest coordinates, in the world.</summary>
        public Vector3 Maxs { get; set; }

        public Vector3 EdgeFade { get; set; }
        public int Priority { get; set; }
    }

    private sealed class FaceBounds
    {
        public Vector3 Min = new(float.MaxValue);
        public Vector3 Max = new(float.MinValue);
        public int Count;
    }

    /// <summary>Fits a box to each sample.</summary>
    /// <param name="bsp">The map.</param>
    /// <param name="origins">The origins of the samples.</param>
    /// <returns>The volumes, in the order of the origins.</returns>
    public static List<Volume> Fit(BspFile bsp, IReadOnlyList<Vector3> origins)
    {
        var volumes = new List<Volume>(origins.Count);

        if (origins.Count == 0 || bsp.Models.Length == 0)
        {
            return volumes;
        }

        var trace = new BspWorldTrace(bsp);
        var world = bsp.Models[0];
        var maxDistance = (world.Max - world.Min).Length();
        var faces = CollectFaces(bsp, trace, origins);
        var directions = CreateDirections();

        for (var i = 0; i < origins.Count; i++)
        {
            var nearest = GetNearestDistance(origins, i);
            volumes.Add(FitVolume(trace, world, origins[i], faces[i], directions, maxDistance, nearest));
        }

        AssignPriorities(volumes);
        Trim(volumes);
        SetEdgeFades(volumes);

        return volumes;
    }

    // Faces that use a sample have a material patched for it, named after the original material and the origin of the sample
    private static FaceBounds[] CollectFaces(BspFile bsp, BspWorldTrace trace, IReadOnlyList<Vector3> origins)
    {
        var bounds = new FaceBounds[origins.Count];
        var samplesByOrigin = new Dictionary<(int, int, int), int>();

        for (var i = 0; i < origins.Count; i++)
        {
            bounds[i] = new FaceBounds();
            samplesByOrigin.TryAdd(RoundOrigin(origins[i]), i);
        }

        var prefix = $"maps/{bsp.Name}/";
        var sampleByTextureInfo = new int[bsp.TextureInfos.Length];

        for (var i = 0; i < sampleByTextureInfo.Length; i++)
        {
            sampleByTextureInfo[i] = -1;
            var dataIndex = bsp.TextureInfos[i].TexData;

            if (dataIndex < 0 || dataIndex >= bsp.TextureData.Length)
            {
                continue;
            }

            var name = bsp.GetTextureName(bsp.TextureData[dataIndex].NameStringTableId);

            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && TryParseOrigin(name, out var origin)
                && samplesByOrigin.TryGetValue(origin, out var sample))
            {
                sampleByTextureInfo[i] = sample;
            }
        }

        var world = bsp.Models[0];
        var allFaces = bsp.Faces;
        var lastFace = Math.Min(world.FirstFace + world.NumFaces, allFaces.Length);

        for (var i = world.FirstFace; i < lastFace; i++)
        {
            var face = allFaces[i];

            if (face.TexInfo < 0 || face.TexInfo >= sampleByTextureInfo.Length || sampleByTextureInfo[face.TexInfo] < 0)
            {
                continue;
            }

            var sample = sampleByTextureInfo[face.TexInfo];
            AddFace(bsp, trace, face, origins[sample], bounds[sample]);
        }

        return bounds;
    }

    private static void AddFace(BspFile bsp, BspWorldTrace trace, in BspFace face, Vector3 origin, FaceBounds bounds)
    {
        if (face.NumEdges < 3)
        {
            return;
        }

        var normal = bsp.Planes[face.PlaneNum].Normal;

        if (face.Side != 0)
        {
            normal = -normal;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var center = Vector3.Zero;

        for (var i = 0; i < face.NumEdges; i++)
        {
            var vertex = bsp.GetVertexFromSurfEdge(face.FirstEdge + i);
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
            center += vertex;
        }

        center /= face.NumEdges;

        // The face itself blocks a trace that ends on it, so the trace ends just in front of it
        var hit = trace.Trace(origin, center + (normal * FaceNormalOffset));

        if (hit.Fraction < 1f)
        {
            return;
        }

        bounds.Min = Vector3.Min(bounds.Min, min);
        bounds.Max = Vector3.Max(bounds.Max, max);
        bounds.Count++;
    }

    // The three numbers at the end of the name of a patched material
    private static bool TryParseOrigin(string name, out (int, int, int) origin)
    {
        origin = default;
        var parts = name.Split('_');

        if (parts.Length < 4)
        {
            return false;
        }

        var style = NumberStyles.AllowLeadingSign;
        var culture = CultureInfo.InvariantCulture;

        if (!int.TryParse(parts[^3], style, culture, out var x)
            || !int.TryParse(parts[^2], style, culture, out var y)
            || !int.TryParse(parts[^1], style, culture, out var z))
        {
            return false;
        }

        origin = (x, y, z);
        return true;
    }

    private static (int, int, int) RoundOrigin(Vector3 origin)
        => ((int)MathF.Round(origin.X), (int)MathF.Round(origin.Y), (int)MathF.Round(origin.Z));

    // Directions spread evenly over the sphere
    private static Vector3[] CreateDirections()
    {
        var directions = new Vector3[RayCount];
        var goldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));

        for (var i = 0; i < RayCount; i++)
        {
            var z = 1f - (2f * (i + 0.5f) / RayCount);
            var radius = MathF.Sqrt(1f - (z * z));
            var angle = i * goldenAngle;
            directions[i] = new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), z);
        }

        return directions;
    }

    private static float GetNearestDistance(IReadOnlyList<Vector3> origins, int index)
    {
        var nearest = float.PositiveInfinity;

        for (var i = 0; i < origins.Count; i++)
        {
            if (i != index)
            {
                nearest = MathF.Min(nearest, Vector3.Distance(origins[index], origins[i]));
            }
        }

        return nearest;
    }

    // Sides are numbered 2 * axis for the positive direction of an axis and 2 * axis + 1 for the negative one
    private static int GetSide(Vector3 direction)
    {
        for (var axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(direction[axis]) >= AxisConeCosine)
            {
                return (axis * 2) + (direction[axis] < 0f ? 1 : 0);
            }
        }

        return -1;
    }

    // How far the wall is along the axis of the ray, or infinity where the ray leaves the map or ends in the sky
    private static float GetWallDistance(BspWorldTrace trace, Vector3 origin, Vector3 direction, float maxDistance)
    {
        var hit = trace.Trace(origin, origin + (direction * maxDistance));

        if (hit.Fraction >= 1f)
        {
            return float.PositiveInfinity;
        }

        var distance = hit.Fraction * maxDistance;

        if (trace.IsSkyHit(hit, origin + (direction * distance)))
        {
            return float.PositiveInfinity;
        }

        var axis = GetSide(direction) / 2;

        return distance * MathF.Abs(direction[axis]);
    }

    private static float GetFaceExtent(FaceBounds bounds, Vector3 origin, int side)
    {
        if (bounds.Count == 0)
        {
            return 0f;
        }

        var axis = side / 2;
        var extent = (side % 2 == 0) ? bounds.Max[axis] - origin[axis] : origin[axis] - bounds.Min[axis];

        return MathF.Max(extent, 0f);
    }

    private static Volume FitVolume(
        BspWorldTrace trace,
        BspModel world,
        Vector3 origin,
        FaceBounds faces,
        Vector3[] directions,
        float maxDistance,
        float nearestDistance)
    {
        var wallDistances = new List<float>[6];

        for (var i = 0; i < wallDistances.Length; i++)
        {
            wallDistances[i] = [];
        }

        foreach (var direction in directions)
        {
            var side = GetSide(direction);

            if (side >= 0)
            {
                wallDistances[side].Add(GetWallDistance(trace, origin, direction, maxDistance));
            }
        }

        var extents = new float[6];
        var neighbourReach = MathF.Min(nearestDistance * NeighbourReach, MaxOpenHalfExtent);

        for (var side = 0; side < extents.Length; side++)
        {
            var distances = wallDistances[side];
            distances.Sort();

            var wall = distances[(int)(WallPercentile * (distances.Count - 1))];
            var faceExtent = GetFaceExtent(faces, origin, side);
            float extent;

            if (float.IsFinite(wall))
            {
                extent = MathF.Max(wall, faceExtent);
            }
            else
            {
                extent = MathF.Min(MathF.Max(faceExtent, neighbourReach), MaxOpenHalfExtent);
            }

            extents[side] = MathF.Max(extent, MinHalfExtent);
        }

        var mins = origin - new Vector3(extents[1], extents[3], extents[5]);
        var maxs = origin + new Vector3(extents[0], extents[2], extents[4]);

        // The box stays inside the world but always holds the sample
        mins = Vector3.Min(origin, Vector3.Max(mins, world.Min));
        maxs = Vector3.Max(origin, Vector3.Min(maxs, world.Max));

        return new Volume
        {
            Origin = origin,
            Mins = mins,
            Maxs = maxs,
        };
    }

    private static bool Contains(Volume outer, Volume inner)
    {
        for (var axis = 0; axis < 3; axis++)
        {
            if (outer.Mins[axis] > inner.Mins[axis] + Tolerance || outer.Maxs[axis] < inner.Maxs[axis] - Tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static float GetBoxVolume(Volume volume)
    {
        var size = volume.Maxs - volume.Mins;

        return size.X * size.Y * size.Z;
    }

    // A volume that is inside a larger one wins over it, one level per volume around it
    private static void AssignPriorities(List<Volume> volumes)
    {
        foreach (var volume in volumes)
        {
            var volumeSize = GetBoxVolume(volume);
            var containers = 0;

            foreach (var other in volumes)
            {
                if (other != volume && GetBoxVolume(other) > volumeSize && Contains(other, volume))
                {
                    containers++;
                }
            }

            volume.Priority = Math.Min(OutermostPriority + containers, InnermostPriority);
        }
    }

    // Boxes of equal priority that overlap side by side are cut apart where their samples are halfway.
    // Every cut is taken from the boxes as they were fitted, so the result does not depend on the order.
    private static void Trim(List<Volume> volumes)
    {
        var mins = volumes.Select(volume => volume.Mins).ToArray();
        var maxs = volumes.Select(volume => volume.Maxs).ToArray();

        for (var i = 0; i < volumes.Count; i++)
        {
            for (var j = i + 1; j < volumes.Count; j++)
            {
                var a = volumes[i];
                var b = volumes[j];

                if (a.Priority != b.Priority || Contains(a, b) || Contains(b, a))
                {
                    continue;
                }

                if (!TryGetCut(a, b, out var axis, out var cut, out var aIsLower))
                {
                    continue;
                }

                var lower = aIsLower ? i : j;
                var upper = aIsLower ? j : i;
                maxs[lower][axis] = MathF.Min(maxs[lower][axis], cut);
                mins[upper][axis] = MathF.Max(mins[upper][axis], cut);
            }
        }

        for (var i = 0; i < volumes.Count; i++)
        {
            volumes[i].Mins = mins[i];
            volumes[i].Maxs = maxs[i];
        }
    }

    private static bool TryGetCut(Volume a, Volume b, out int axis, out float cut, out bool aIsLower)
    {
        axis = 0;
        cut = 0f;
        aIsLower = true;

        var largestSeparation = -1f;

        for (var candidate = 0; candidate < 3; candidate++)
        {
            var overlap = MathF.Min(a.Maxs[candidate], b.Maxs[candidate]) - MathF.Max(a.Mins[candidate], b.Mins[candidate]);

            if (overlap <= Tolerance)
            {
                return false;
            }

            // The boxes are side by side along the axis that the samples are furthest apart on
            var separation = MathF.Abs(a.Origin[candidate] - b.Origin[candidate]);

            if (separation > largestSeparation)
            {
                largestSeparation = separation;
                axis = candidate;
            }
        }

        aIsLower = a.Origin[axis] <= b.Origin[axis];
        var lowerOrigin = aIsLower ? a.Origin[axis] : b.Origin[axis];
        var upperOrigin = aIsLower ? b.Origin[axis] : a.Origin[axis];
        var overlapMin = MathF.Max(a.Mins[axis], b.Mins[axis]);
        var overlapMax = MathF.Min(a.Maxs[axis], b.Maxs[axis]);

        cut = Math.Clamp((lowerOrigin + upperOrigin) * 0.5f, overlapMin, overlapMax);

        return true;
    }

    // The fade on an axis is a share of the distance to the nearest volume that touches the box
    private static void SetEdgeFades(List<Volume> volumes)
    {
        foreach (var volume in volumes)
        {
            var halfSize = (volume.Maxs - volume.Mins) * 0.5f;
            var fade = Vector3.Zero;

            for (var axis = 0; axis < 3; axis++)
            {
                var nearest = halfSize[axis] * 2f;

                foreach (var other in volumes)
                {
                    if (other != volume && Touches(volume, other))
                    {
                        nearest = MathF.Min(nearest, MathF.Abs(other.Origin[axis] - volume.Origin[axis]));
                    }
                }

                fade[axis] = MathF.Min(MathF.Max(nearest * EdgeFadeShare, MinEdgeFade), halfSize[axis]);
            }

            volume.EdgeFade = fade;
        }
    }

    private static bool Touches(Volume a, Volume b)
    {
        for (var axis = 0; axis < 3; axis++)
        {
            if (a.Mins[axis] > b.Maxs[axis] + Tolerance || b.Mins[axis] > a.Maxs[axis] + Tolerance)
            {
                return false;
            }
        }

        return true;
    }
}
