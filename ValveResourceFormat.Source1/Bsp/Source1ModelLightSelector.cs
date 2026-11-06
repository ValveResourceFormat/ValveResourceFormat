using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Picks the world lights that light a model at a point the way the game does, and gives the light entities
/// that some point picked an index the light probes refer to them by.
/// </summary>
/// <remarks>
/// A model takes at most <see cref="MaxDirectLights"/> lights as direct lights, the brightest at its lighting
/// origin. The sun only lights a point in a leaf that sees the sky, along a line that reaches a sky face.
/// Other lights must be in a cluster the leaf of the point can see, bright enough, and not blocked on the way.
/// Lights the map compiler already put into the ambient cubes of the leaves light models through them only.
/// The lights that pass but are not among the brightest, and lights without an entity to index, are added
/// to the ambient cube of the point.
/// </remarks>
internal sealed class Source1ModelLightSelector
{
    /// <summary>The number of direct lights a model takes.</summary>
    public const int MaxDirectLights = 4;

    // A light index is stored in one byte of the probe textures
    private const int MaxIndexedLights = 255;

    // Index of the last sun shadow cascade, past the count any viewer fits
    private const long LastSunCascade = 3;

    private const float MinLightIntensity = 0.0002f;
    private const float SunTraceDistance = 57016.32f;
    private const float DirectVisibilityDistance = 8f;

    // Lights the map compiler added to the ambient cubes of the leaves
    private const int InAmbientCubeFlag = 0x1;

    // Leaves that see the 3D sky or the 2D sky
    private const int LeafSkyFlags = 0x5;

    private static readonly Vector3 LuminanceWeights = new(0.299f, 0.587f, 0.114f);

    private static readonly Vector3[] CubeAxes =
    [
        Vector3.UnitX, -Vector3.UnitX,
        Vector3.UnitY, -Vector3.UnitY,
        Vector3.UnitZ, -Vector3.UnitZ,
    ];

    private enum EmitType
    {
        Surface = 0,
        Point = 1,
        Spot = 2,
        Sky = 3,
        Quake = 4,
        SkyAmbient = 5,
    }

    internal sealed class Light(BspWorldLight data, EntityLump.Entity? entity)
    {
        public BspWorldLight Data { get; } = data;

        public EntityLump.Entity? Entity { get; } = entity;

        public int Index { get; set; } = -1;
    }

    internal readonly record struct Candidate(Light Light, Vector3 Color, Vector3 Direction, float Luminance);

    /// <summary>The lights that reach one point, brightest first.</summary>
    public sealed class Selection
    {
        internal List<Candidate> Candidates { get; } = [];
    }

    private readonly BspWorldTrace trace;
    private readonly BspLeaf[] leafs;
    private readonly List<Light> lights = [];
    private readonly List<Light> indexedLights = [];
    private Light[] skyLights = [];
    private Light[][] lightsByCluster = [];

    /// <summary>Reads the world lights of a map and finds the converted light entity of each.</summary>
    /// <param name="bsp">The map.</param>
    /// <param name="entities">The converted entities of the map.</param>
    public Source1ModelLightSelector(BspFile bsp, IReadOnlyList<EntityLump.Entity> entities)
    {
        trace = new BspWorldTrace(bsp);
        leafs = bsp.Leafs;

        var worldLights = bsp.HasHdrLighting && bsp.WorldLightsHdr.Length > 0 ? bsp.WorldLightsHdr : bsp.WorldLights;
        var sun = FindEntity(entities, "light_environment");
        var lightEntities = FindLightEntities(entities);

        foreach (var worldLight in worldLights)
        {
            var type = (EmitType)worldLight.EmitType;

            if (type == EmitType.SkyAmbient || (worldLight.Flags & InAmbientCubeFlag) != 0)
            {
                continue;
            }

            var entity = type switch
            {
                EmitType.Sky => sun,
                EmitType.Point or EmitType.Spot or EmitType.Quake => FindLightEntity(lightEntities, worldLight.Origin),
                _ => null,
            };

            lights.Add(new Light(worldLight, entity));
        }

        CreateClusterLights(bsp.Visibility);
    }

    // The lights that a point in each cluster can see, so that a point does not test every light of the map.
    // The sky light has no position and is judged by the leaf and a trace instead, so every cluster has it.
    private void CreateClusterLights(BspVisibility visibility)
    {
        lightsByCluster = new Light[visibility.ClusterCount][];

        var sky = new List<Light>();

        foreach (var light in lights)
        {
            if ((EmitType)light.Data.EmitType == EmitType.Sky)
            {
                sky.Add(light);
            }
        }

        skyLights = [.. sky];

        for (var cluster = 0; cluster < lightsByCluster.Length; cluster++)
        {
            var visible = new List<Light>();

            foreach (var light in lights)
            {
                var isSky = (EmitType)light.Data.EmitType == EmitType.Sky;

                if (isSky || (light.Data.Cluster >= 0 && visibility.IsVisible(cluster, light.Data.Cluster)))
                {
                    visible.Add(light);
                }
            }

            lightsByCluster[cluster] = [.. visible];
        }
    }

    /// <summary>Gets whether the map has lights that can light models directly.</summary>
    public bool HasLights => lights.Count > 0;

    private static EntityLump.Entity? FindEntity(IReadOnlyList<EntityLump.Entity> entities, string classname)
    {
        foreach (var entity in entities)
        {
            if (entity.GetStringProperty("classname") == classname)
            {
                return entity;
            }
        }

        return null;
    }

    // The entities of lights that a world light can stand for
    private static List<EntityLump.Entity> FindLightEntities(IReadOnlyList<EntityLump.Entity> entities)
    {
        var result = new List<EntityLump.Entity>();

        foreach (var entity in entities)
        {
            var classname = entity.GetStringProperty("classname");

            if (classname is ("light_omni" or "light_spot") && !entity.ContainsKey("lightcookie"))
            {
                result.Add(entity);
            }
        }

        return result;
    }

    private static EntityLump.Entity? FindLightEntity(List<EntityLump.Entity> lightEntities, Vector3 origin)
    {
        const float OriginTolerance = 0.5f;

        foreach (var entity in lightEntities)
        {
            if (Vector3.DistanceSquared(entity.GetVector3Property("origin"), origin) <= OriginTolerance * OriginTolerance)
            {
                return entity;
            }
        }

        return null;
    }

    /// <summary>Finds the lights that reach a point, brightest first. This only reads the selector, so points can be gathered on several threads.</summary>
    /// <param name="leafIndex">The leaf of the world model that contains the point, or -1 for a map without nodes.</param>
    /// <param name="point">The lighting origin.</param>
    /// <param name="selection">Receives the lights. Each thread needs its own.</param>
    public void Gather(int leafIndex, Vector3 point, Selection selection)
    {
        var candidates = selection.Candidates;
        candidates.Clear();

        if (leafIndex < 0)
        {
            return;
        }

        var leaf = leafs[leafIndex];
        var reachable = (uint)leaf.Cluster < (uint)lightsByCluster.Length ? lightsByCluster[leaf.Cluster] : skyLights;

        foreach (var light in reachable)
        {
            if (TryGetCandidate(light, leaf, point, out var candidate))
            {
                candidates.Add(candidate);
            }
        }

        candidates.Sort(static (a, b) => b.Luminance.CompareTo(a.Luminance));
    }

    /// <summary>Picks the direct lights of a gathered point and adds the other lights to its ambient cube. Points must be applied in the same order every run, as the first to take a light gives it its index.</summary>
    /// <param name="selection">The lights gathered for the point.</param>
    /// <param name="cube">The ambient cube at the point, in the order +X, -X, +Y, -Y, +Z, -Z, which receives the lights that are not direct.</param>
    /// <param name="directLights">Receives the indices of the direct lights, -1 for an unused slot.</param>
    public void Apply(Selection selection, Span<Vector3> cube, Span<int> directLights)
    {
        directLights.Fill(-1);

        var slot = 0;

        foreach (var candidate in selection.Candidates)
        {
            if (slot < MaxDirectLights && TryGetIndex(candidate.Light, out var index))
            {
                directLights[slot] = index;
                slot++;
                continue;
            }

            AddToCube(cube, candidate);
        }
    }

    private bool TryGetCandidate(Light light, in BspLeaf leaf, Vector3 point, out Candidate candidate)
    {
        candidate = default;

        var data = light.Data;
        var type = (EmitType)data.EmitType;

        if (type == EmitType.Sky)
        {
            if ((leaf.Flags & LeafSkyFlags) == 0 || !SeesSky(point, data.Normal))
            {
                return false;
            }

            candidate = new Candidate(light, data.Intensity, -data.Normal, Vector3.Dot(data.Intensity, LuminanceWeights));
            return true;
        }

        var toLight = data.Origin - point;
        var distance = toLight.Length();
        var direction = distance > 0f ? toLight / distance : Vector3.UnitZ;
        var attenuation = GetAttenuation(data, type, distance, direction);
        var color = data.Intensity * attenuation;
        var luminance = Vector3.Dot(color, LuminanceWeights);

        // Surface lights are kept however dim they are
        if (type != EmitType.Surface && luminance < MinLightIntensity)
        {
            return false;
        }

        if (!IsVisibleFrom(data.Origin, point, distance))
        {
            return false;
        }

        candidate = new Candidate(light, color, direction, luminance);
        return true;
    }

    private static float GetAttenuation(in BspWorldLight light, EmitType type, float distance, Vector3 direction)
    {
        if (light.Radius > 0f && distance > light.Radius)
        {
            return 0f;
        }

        var distanceSquared = MathF.Max(distance * distance, 1f);

        if (type == EmitType.Surface)
        {
            return MathF.Max(Vector3.Dot(light.Normal, -direction), 0f) / distanceSquared;
        }

        var falloff = light.ConstantAttenuation + (light.LinearAttenuation * distance) + (light.QuadraticAttenuation * distanceSquared);
        var attenuation = falloff > 0f ? 1f / falloff : 0f;

        if (type != EmitType.Spot)
        {
            return attenuation;
        }

        var coneDot = Vector3.Dot(light.Normal, -direction);

        if (coneDot <= light.StopDot2)
        {
            return 0f;
        }

        if (coneDot < light.StopDot)
        {
            var edge = (coneDot - light.StopDot2) / (light.StopDot - light.StopDot2);

            if (light.Exponent != 0f && light.Exponent != 1f)
            {
                edge = MathF.Pow(edge, light.Exponent);
            }

            attenuation *= edge;
        }

        return attenuation;
    }

    private bool SeesSky(Vector3 point, Vector3 lightDirection)
    {
        var end = point - (lightDirection * SunTraceDistance);
        var hit = trace.Trace(point, end);

        if (hit.Fraction >= 1f)
        {
            return false;
        }

        return trace.IsSkyHit(hit, Vector3.Lerp(point, end, hit.Fraction));
    }

    // A light still reaches a point when what blocks it is close enough to the point
    private bool IsVisibleFrom(Vector3 lightOrigin, Vector3 point, float distance)
    {
        var hit = trace.Trace(lightOrigin, point);

        return distance * (1f - hit.Fraction) <= DirectVisibilityDistance;
    }

    private bool TryGetIndex(Light light, out int index)
    {
        index = light.Index;

        if (index >= 0)
        {
            return true;
        }

        if (light.Entity == null || indexedLights.Count >= MaxIndexedLights)
        {
            return false;
        }

        index = indexedLights.Count;
        light.Index = index;
        indexedLights.Add(light);

        return true;
    }

    private static void AddToCube(Span<Vector3> cube, in Candidate candidate)
    {
        for (var face = 0; face < CubeAxes.Length; face++)
        {
            var facing = MathF.Max(Vector3.Dot(CubeAxes[face], candidate.Direction), 0f);
            cube[face] += candidate.Color * facing;
        }
    }

    /// <summary>
    /// Gives the light entities that a point picked their index, and leaves the other light entities of world
    /// lights out of model lighting.
    /// </summary>
    public void ApplyToEntities()
    {
        foreach (var light in lights)
        {
            if (light.Entity == null)
            {
                continue;
            }

            // An animated light is left out of the lightmaps and stays a dynamic light
            if (BspLightStyles.IsAnimated(light.Entity.GetStringProperty("pattern") ?? string.Empty))
            {
                continue;
            }

            light.Entity["directlight"] = 1L;

            // A light that no model takes is not stored for the renderer
            if (light.Index < 0)
            {
                continue;
            }

            light.Entity["bakelightindex"] = (long)light.Index;

            if ((EmitType)light.Data.EmitType == EmitType.Sky)
            {
                // The lightmap carries the world's sun shadows, so the sun cascades add only the static props
                light.Entity["cascaderenderstaticobj"] = LastSunCascade;
            }

            if (GetStrengthScale(light.Entity) is { } strengthScale)
            {
                light.Entity["bakelightindexscale"] = (double)strengthScale;
            }
        }
    }

    // The brightness of a light is the light 100 units away. A full strength of an indexed light stands for its
    // attenuation terms at 100 units without the constant term, which this scale makes the whole curve there.
    private static float? GetStrengthScale(EntityLump.Entity entity)
    {
        var classname = entity.GetStringProperty("classname");

        if (classname is not ("light_omni" or "light_spot"))
        {
            return null;
        }

        var constant = entity.GetFloatProperty("attenuation0");
        var linear = entity.GetFloatProperty("attenuation1");
        var quadratic = entity.GetFloatProperty("attenuation2");
        var withoutConstant = (linear * 100f) + (quadratic * 10000f);

        if (withoutConstant <= 0f)
        {
            return null;
        }

        return (constant + withoutConstant) / withoutConstant;
    }
}
