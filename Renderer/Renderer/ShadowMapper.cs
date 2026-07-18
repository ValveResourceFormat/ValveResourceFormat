using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer;

/// <summary>A visible light produced by a <see cref="ShadowMapper"/> pass.</summary>
public struct BinnedLight
{
    /// <summary>Gets or sets the scene light.</summary>
    public SceneLight Light { get; set; }
    /// <summary>Gets or sets the shadow face width in texels, or 0 when the light casts no shadows.</summary>
    public int FaceWidth { get; set; }
    /// <summary>Gets or sets the shadow face height in texels.</summary>
    public int FaceHeight { get; set; }
    /// <summary>Gets or sets the first index into the shadow mapper's face placements, or -1 when none were assigned.</summary>
    public int FirstFaceIndex { get; set; }

    /// <summary>Gets whether this light requested shadow map space.</summary>
    public readonly bool WantsShadows => FaceWidth > 0;
    /// <summary>Gets whether this light was assigned face placements.</summary>
    public readonly bool HasShadows => FirstFaceIndex >= 0;
}

/// <summary>A shadow-casting light face queued for rendering into the shadow atlas.</summary>
public struct BinnedShadowCaster
{
    /// <summary>Gets or sets the gutter-adjusted world-to-frustum transform for this shadow face.</summary>
    public Matrix4x4 WorldToFrustum { get; set; }
    /// <summary>Gets or sets the atlas region allocated for this shadow face.</summary>
    public ShadowAtlasRegion Region { get; set; }
    /// <summary>Gets or sets the scene light that owns this shadow caster.</summary>
    public SceneLight Light { get; set; }
    /// <summary>Gets or sets the face index within the light's faces array.</summary>
    public int FaceIndex { get; set; }
}

/// <summary>The atlas placement of a single binned shadow face: its region and the matching shadow offset-scale.</summary>
public readonly record struct ShadowFacePlacement(ShadowAtlasRegion Region, Vector4 OffsetScale);

/// <summary>Culls and sizes lights and packs their shadow faces into the atlas each frame.</summary>
public class ShadowMapper
{
    private const float ShadowTexelsPerPixel = 1f;
    private const int MinShadowFaceSize = 128;
    private const float AtlasAreaBudget = 0.85f;
    private const int ShadowGutterTexels = 2;

    private readonly ShadowAtlasPacker shadowAtlas = new();
    private readonly ShadowFacePlacement[] facePlacements = new ShadowFacePlacement[BarnLightConstants.MAX_BARN_LIGHTS];

    private BinnedLight[] candidates = new BinnedLight[BarnLightConstants.MAX_BARN_LIGHTS];
    private (float Importance, int CandidateIndex)[] importanceOrder = new (float, int)[BarnLightConstants.MAX_BARN_LIGHTS];
    private int candidateCount;

    /// <summary>Gets the lights binned by the last <see cref="Bin"/> call, in deterministic scene order.</summary>
    public ReadOnlySpan<BinnedLight> BinnedLights => candidates.AsSpan(0, candidateCount);

    /// <summary>Gets the shadow faces queued for rendering into the atlas by the last <see cref="Bin"/> call.</summary>
    public List<BinnedShadowCaster> ShadowCasters { get; } = new(BarnLightConstants.MAX_BARN_LIGHTS);

    /// <summary>Gets the atlas placement for an absolute face index (<see cref="BinnedLight.FirstFaceIndex"/> plus face index).</summary>
    public ShadowFacePlacement GetFacePlacement(int index) => facePlacements[index];

    /// <summary>Culls the given lights against the camera and allocates shadow atlas regions for the visible casters.</summary>
    /// <param name="lights">The dynamic lights considered for binning.</param>
    /// <param name="camera">The camera used for culling and shadow resolution selection.</param>
    /// <param name="atlasSize">Pixel size of the shadow atlas texture.</param>
    /// <param name="cookiePaths">Map from cookie material path to cookie atlas index, used when recomputing dirty faces.</param>
    public void Bin(List<SceneLight> lights, Camera camera, int atlasSize, Dictionary<string, int> cookiePaths)
    {
        candidateCount = 0;
        ShadowCasters.Clear();

        var cameraFrustum = camera.ViewFrustum;
        var pixelsPerUnit = camera.ProjectionMatrix.M22 * camera.WindowSize.Y * 0.5f;

        if (candidates.Length < lights.Count)
        {
            candidates = new BinnedLight[lights.Count];
            importanceOrder = new (float, int)[lights.Count];
        }

        var importanceCount = 0;

        foreach (var light in lights)
        {
            if (light.PrecomputedFieldsValid && !cameraFrustum.Intersects(light.PrecomputedBounds))
            {
                continue;
            }

            if (light.IsDirty)
            {
                light.ComputeBarnFaces(cookiePaths);
                light.IsDirty = false;
            }

            if (!light.IsVisible)
            {
                continue;
            }

            var candidate = new BinnedLight { Light = light, FirstFaceIndex = -1 };

            if (light.CastShadows > 0)
            {
                // TODO: demote omni faces that don't face the camera.
                var importance = ComputeShadowFaceSize(ref candidate, camera.Location, pixelsPerUnit, atlasSize);
                importanceOrder[importanceCount++] = (importance, candidateCount);
            }

            candidates[candidateCount++] = candidate;
        }

        var candidateSpan = candidates.AsSpan(0, candidateCount);

        var order = importanceOrder.AsSpan(0, importanceCount);
        order.Sort();

        // Hysteresis runs after demotion so stable sizes win over small budget corrections.
        DemoteToBudget(candidateSpan, order, atlasSize);
        ApplyHysteresis(candidateSpan, atlasSize);
        AssignRegions(candidateSpan, order, atlasSize);
    }

    private static Vector4 ComputeShadowOffsetScale(ShadowAtlasRegion region, int atlasSize, ref Matrix4x4 shadowMatrix)
    {
        var scale = new Vector2(region.Width, region.Height) / atlasSize * 0.5f;
        var offset = new Vector2(region.X + region.Width / 2f, region.Y + region.Height / 2f) / atlasSize;

        var shrink = new Vector2(
            (float)(region.Width - ShadowGutterTexels * 2) / region.Width,
            (float)(region.Height - ShadowGutterTexels * 2) / region.Height
        );
        scale *= shrink;
        shadowMatrix *= Matrix4x4.CreateScale(shrink.X, shrink.Y, 1f);

        return new Vector4(offset.X, offset.Y, scale.X, scale.Y);
    }

    private static int RoundToCell(int value)
    {
        const int cell = ShadowAtlasPacker.CellSize;
        return Math.Max((value + cell / 2) / cell * cell, cell);
    }

    private static float ComputeShadowFaceSize(ref BinnedLight candidate, Vector3 cameraPosition, float pixelsPerUnit, int atlasSize)
    {
        var light = candidate.Light;
        Vector3 boundsCenter;
        float boundsRadius;

        if (light.PrecomputedFieldsValid)
        {
            var bounds = light.PrecomputedBounds;
            boundsCenter = bounds.Center;
            boundsRadius = bounds.Size.Length() * 0.5f;
        }
        else
        {
            boundsCenter = light.Position;
            boundsRadius = light.Range;
        }

        var distance = Vector3.Distance(cameraPosition, boundsCenter);
        var importance = boundsRadius / MathF.Max(distance - boundsRadius, 1f) * pixelsPerUnit;

        var idealSize = MathF.Min(importance * 2f * ShadowTexelsPerPixel, atlasSize);
        var maxSize = Math.Clamp(RoundToCell(light.ShadowMapSize), MinShadowFaceSize, atlasSize);
        var targetSize = Math.Clamp(RoundToCell((int)idealSize), MinShadowFaceSize, maxSize);

        SetFaceDimensions(ref candidate, targetSize, atlasSize);
        return importance;
    }

    private static void SetFaceDimensions(ref BinnedLight candidate, int longAxis, int atlasSize)
    {
        var (w, h) = candidate.Light.GetShadowFaceDimensions(longAxis);
        candidate.FaceWidth = Math.Clamp(RoundToCell(w), MinShadowFaceSize, atlasSize);
        candidate.FaceHeight = Math.Clamp(RoundToCell(h), MinShadowFaceSize, atlasSize);
    }

    private static void ApplyHysteresis(Span<BinnedLight> candidateSpan, int atlasSize)
    {
        foreach (ref var candidate in candidateSpan)
        {
            if (!candidate.WantsShadows)
            {
                continue;
            }

            var size = Math.Max(candidate.FaceWidth, candidate.FaceHeight);
            var previous = candidate.Light.AdaptiveShadowSize;
            var deadband = Math.Max(ShadowAtlasPacker.CellSize * 2, previous / 8);

            if (previous >= MinShadowFaceSize && previous <= atlasSize && Math.Abs(size - previous) < deadband)
            {
                size = previous;
                SetFaceDimensions(ref candidate, size, atlasSize);
            }

            candidate.Light.AdaptiveShadowSize = size;
        }
    }

    private static void ShrinkFace(ref int width, ref int height)
    {
        if (width >= height)
        {
            var newWidth = Math.Max(width - ShadowAtlasPacker.CellSize, MinShadowFaceSize);
            height = Math.Clamp(RoundToCell(height * newWidth / width), MinShadowFaceSize, newWidth);
            width = newWidth;
        }
        else
        {
            var newHeight = Math.Max(height - ShadowAtlasPacker.CellSize, MinShadowFaceSize);
            width = Math.Clamp(RoundToCell(width * newHeight / height), MinShadowFaceSize, newHeight);
            height = newHeight;
        }
    }

    private static void DemoteToBudget(Span<BinnedLight> candidateSpan, ReadOnlySpan<(float Importance, int CandidateIndex)> order, int atlasSize)
    {
        var budget = (long)(atlasSize * (float)atlasSize * AtlasAreaBudget);
        var total = 0L;

        foreach (ref readonly var candidate in candidateSpan)
        {
            if (candidate.WantsShadows)
            {
                total += (long)candidate.FaceWidth * candidate.FaceHeight * candidate.Light.BarnFaces.Length;
            }
        }

        if (total <= budget)
        {
            return;
        }

        var scale = MathF.Sqrt(budget / (float)total);
        total = 0;

        foreach (ref var candidate in candidateSpan)
        {
            if (!candidate.WantsShadows)
            {
                continue;
            }

            var size = (int)(Math.Max(candidate.FaceWidth, candidate.FaceHeight) * scale);
            SetFaceDimensions(ref candidate, Math.Max(size, MinShadowFaceSize), atlasSize);
            total += (long)candidate.FaceWidth * candidate.FaceHeight * candidate.Light.BarnFaces.Length;
        }

        if (total <= budget)
        {
            return;
        }

        foreach (var (_, index) in order)
        {
            ref var candidate = ref candidateSpan[index];
            var faceCount = candidate.Light.BarnFaces.Length;

            while (total > budget && Math.Max(candidate.FaceWidth, candidate.FaceHeight) > MinShadowFaceSize)
            {
                var oldArea = (long)candidate.FaceWidth * candidate.FaceHeight * faceCount;

                var width = candidate.FaceWidth;
                var height = candidate.FaceHeight;
                ShrinkFace(ref width, ref height);
                candidate.FaceWidth = width;
                candidate.FaceHeight = height;

                total += (long)width * height * faceCount - oldArea;
            }

            if (total <= budget)
            {
                return;
            }
        }
    }

    private void AssignRegions(Span<BinnedLight> candidateSpan, ReadOnlySpan<(float Importance, int CandidateIndex)> order, int atlasSize)
    {
        shadowAtlas.Begin(atlasSize);

        var usedTexels = 0L;
        var assignedFaces = 0;
        for (var i = order.Length - 1; i >= 0; i--)
        {
            ref var candidate = ref candidateSpan[order[i].CandidateIndex];
            var light = candidate.Light;
            var faceCount = light.BarnFaces.Length;

            if (assignedFaces + faceCount > facePlacements.Length)
            {
                continue;
            }

            candidate.FirstFaceIndex = assignedFaces;

            for (var face = 0; face < faceCount; face++)
            {
                var width = candidate.FaceWidth;
                var height = candidate.FaceHeight;

                ShadowAtlasRegion region;
                while (!shadowAtlas.TryAllocate(width, height, out region) && Math.Max(width, height) > MinShadowFaceSize)
                {
                    ShrinkFace(ref width, ref height);
                }

                if (region.IsValid)
                {
                    var shadowMatrix = light.BarnFaces[face].WorldToFrustum;
                    var offsetScale = ComputeShadowOffsetScale(region, atlasSize, ref shadowMatrix);

                    facePlacements[assignedFaces] = new ShadowFacePlacement(region, offsetScale);
                    ShadowCasters.Add(new BinnedShadowCaster
                    {
                        WorldToFrustum = shadowMatrix,
                        Region = region,
                        Light = light,
                        FaceIndex = face,
                    });

                    // Each submitted face is a separate GPU shadow render; an omni light contributes up to 6.
                    PerfStats.Active.Count(Counter.ShadowFaceSubmitted);
                    usedTexels += (long)region.Width * region.Height;
                }
                else
                {
                    facePlacements[assignedFaces] = default;
                }

                assignedFaces++;
            }
        }

        var atlasTexels = (long)atlasSize * atlasSize;
        var atlasUsage = atlasTexels > 0 ? (float)usedTexels / atlasTexels : 0f;

        PerfStats.Active.Set(Metric.ShadowAtlasUsage, atlasUsage);
    }
}
