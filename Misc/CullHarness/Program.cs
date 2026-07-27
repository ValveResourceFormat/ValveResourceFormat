using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Shaders;

const int TileSize = 16;
const int ViewW = 256, ViewH = 128;
const int Cols = ViewW / TileSize;
const int Rows = ViewH / TileSize;
const int DepthBins = 32;
const float KeyRange = 15f;
const float BinWidth = KeyRange / DepthBins;

static Vector2[] Rect(float x0, float y0, float x1, float y1)
{
    const float D = TileSize * 0.5f;
    return
    [
        new(x0 - D, y0 - D),
        new(x1 + D, y0 - D),
        new(x1 + D, y1 + D),
        new(x0 - D, y1 + D),
    ];
}

var polys = new Vector2[][]
{
    Rect(32f, 16f, 79f, 47f),
    Rect(140f, 70f, 240f, 120f),

    Rect(140f, 60f, 200f, 100f),

    [new(128f, 24f), new(184f, 64f), new(128f, 104f), new(72f, 64f)],

    Rect(0f, 0f, 15f, 15f),
};

var keys = new (float Min, float Max)[]
{
    (3.0f, 5.0f),
    (8.0f, 8.4f),
    (2.0f, 3.0f),
    (4.0f, 6.0f),
    (0.1f, 1.0f),
};

var batchOf = new[] { 0, 0, 0, 0, 1 };
var batchCount = new[] { 4, 1, 0 };

var items = new CullItem[64];
var planes = new Vector2[polys.Sum(static poly => poly.Length)];

for (var i = 0; i < items.Length; i++)
{
    items[i] = new CullItem
    {
        BoundsMin = new Vector2(float.MaxValue),
        BoundsMax = new Vector2(float.MinValue),
        DepthMin = float.MaxValue,
        DepthMax = float.MinValue,
    };
}

var slotOf = new int[polys.Length];
var planeCursor = 0;
var nextInBatch = new int[3];

for (var i = 0; i < polys.Length; i++)
{
    var slot = batchOf[i] * 32 + nextInBatch[batchOf[i]]++;
    slotOf[i] = slot;

    var min = new Vector2(float.MaxValue);
    var max = new Vector2(float.MinValue);

    foreach (var v in polys[i])
    {
        min = Vector2.Min(min, v);
        max = Vector2.Max(max, v);
    }

    var area = 0f;
    for (var v = 0; v < polys[i].Length; v++)
    {
        var a = polys[i][v];
        var b = polys[i][(v + 1) % polys[i].Length];
        area += (a.X * b.Y) - (b.X * a.Y);
    }

    if (area <= 0f)
    {
        Console.WriteLine($"  FAIL poly {i} is not counter clockwise (2A = {area})");
        return 1;
    }

    polys[i].CopyTo(planes, planeCursor);

    items[slot] = new CullItem
    {
        BoundsMin = min,
        BoundsMax = max,
        DepthMin = keys[i].Min,
        DepthMax = keys[i].Max,
        FirstPlane = (uint)planeCursor,
        NumPlanes0 = (uint)polys[i].Length,
    };

    planeCursor += polys[i].Length;
}

var cullParams = new CullParams
{
    Tiles = Cols * Rows,
    TilesX = Cols,
    TilesY = Rows,
    TileEpsilon = 0f,
    TileToCenterScale = new Vector2(TileSize),
    TileToCenterOffset = new Vector2(TileSize * 0.5f),
    DepthBins = DepthBins,
    DepthBinWidth = BinWidth,
    BinEpsilon = 0f,
    NearPlane = 0f,
    FirstMaskForBatch0 = 0,
    FirstMaskForBatch1 = 1,
    FirstMaskForBatch2 = 2,
    MaskCount = 2,
};

for (var b = 0; b < 3; b++)
{
    var masks = (batchCount[b] + 31) / 32;
    var entry = new CullBatch
    {
        OutputStride = (uint)masks,
        FirstItem = (uint)(b * 32),
        ItemEnd = (uint)(b * 32 + batchCount[b]),
    };
    cullParams.TileBatches[b] = entry;
    cullParams.BinBatches[b] = entry;
}

var cursor = 0u;
for (var b = 0; b < 3; b++) { cullParams.TileBatches[b].OutputOffset = cursor; cursor += (uint)(Cols * Rows) * cullParams.TileBatches[b].OutputStride; }
for (var b = 0; b < 3; b++) { cullParams.BinBatches[b].OutputOffset = cursor; cursor += DepthBins * cullParams.BinBatches[b].OutputStride; }
var totalWords = (int)cursor;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
var logger = loggerFactory.CreateLogger("harness");

using var window = new OpenTK.Windowing.Desktop.NativeWindow(new()
{
    APIVersion = GLEnvironment.RequiredVersion,
    Flags = OpenTK.Windowing.Common.ContextFlags.ForwardCompatible | OpenTK.Windowing.Common.ContextFlags.Offscreen,
    StartVisible = false,
    Title = "cull harness",
});
window.MakeCurrent();

using var renderContext = new RendererContext(new ValveResourceFormat.IO.GameFileLoader(null, null), logger);
GLEnvironment.Initialize(logger);

var tileShader = renderContext.ShaderLoader.LoadShader("vrf.compute_tile_cullbits");
var binShader = renderContext.ShaderLoader.LoadShader("vrf.compute_depthbin_cullbits");

static int MakeSsbo<T>(int binding, T[] data, int count) where T : struct
{
    GL.CreateBuffers(1, out int handle);
    GL.NamedBufferData(handle, count * Unsafe.SizeOf<T>(), data, BufferUsageHint.DynamicDraw);
    GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, binding, handle);
    return handle;
}

var bits = new uint[totalWords];
for (var i = 0; i < bits.Length; i++) bits[i] = 0xDEADBEEF;

var bitsBuffer = MakeSsbo(13, bits, totalWords);
MakeSsbo(14, items, items.Length);
MakeSsbo(15, planes, planes.Length);

GL.CreateBuffers(1, out int paramsBuffer);
GL.NamedBufferData(paramsBuffer, Unsafe.SizeOf<CullParams>(), ref cullParams, BufferUsageHint.DynamicDraw);
GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 5, paramsBuffer);

tileShader.Use();
GL.DispatchCompute((Cols + 7) / 8, (Rows + 3) / 4, (int)cullParams.MaskCount);
binShader.Use();
GL.DispatchCompute(DepthBins / 32, (int)cullParams.MaskCount, 1);
GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
GL.Finish();

GL.GetNamedBufferSubData(bitsBuffer, IntPtr.Zero, totalWords * sizeof(uint), bits);

var failures = 0;

void Check(string what, bool ok)
{
    if (!ok) { failures++; Console.WriteLine($"  FAIL {what}"); }
}

static bool InsideHull(Vector2 point, Vector2[] poly)
{
    for (var i = 0; i < poly.Length; i++)
    {
        var prev = poly[(i + poly.Length - 1) % poly.Length];
        var cur = poly[i];
        var edge = cur - prev;
        var normal = new Vector2(-edge.Y, edge.X);

        if (Vector2.Dot(normal, point) < Vector2.Dot(normal, cur))
        {
            return false;
        }
    }

    return true;
}

for (var i = 0; i < polys.Length; i++)
{
    var b = batchOf[i];
    var bit = slotOf[i] % 32;
    var stride = cullParams.TileBatches[b].OutputStride;
    var baseWord = cullParams.TileBatches[b].OutputOffset;

    var hits = 0;

    for (var ty = 0; ty < Rows; ty++)
    {
        for (var tx = 0; tx < Cols; tx++)
        {
            var centre = new Vector2(tx * TileSize + TileSize * 0.5f, ty * TileSize + TileSize * 0.5f);
            var expected = InsideHull(centre, polys[i]);

            var word = bits[baseWord + (uint)(ty * Cols + tx) * stride];
            var actual = (word & (1u << bit)) != 0;

            Check($"item {i} tile ({tx},{ty}) expected {expected} got {actual}", expected == actual);
            if (actual) hits++;
        }
    }

    Console.WriteLine($"item {i}: {hits} tiles set");
}

for (var i = 0; i < polys.Length; i++)
{
    var b = batchOf[i];
    var bit = slotOf[i] % 32;
    var stride = cullParams.BinBatches[b].OutputStride;
    var baseWord = cullParams.BinBatches[b].OutputOffset;

    var hits = 0;

    for (var bin = 0; bin < DepthBins; bin++)
    {
        var near = bin * BinWidth;
        var far = (bin + 1) * BinWidth;
        var expected = keys[i].Min <= far && keys[i].Max >= near;

        var word = bits[baseWord + (uint)bin * stride];
        var actual = (word & (1u << bit)) != 0;

        Check($"item {i} bin {bin} expected {expected} got {actual}", expected == actual);
        if (actual) hits++;
    }

    Console.WriteLine($"item {i}: {hits} bins set");
}

{
    var eye = new Vector3(40f, -260f, 90f);
    var target = new Vector3(0f, 40f, 0f);
    var forward = Vector3.Normalize(target - eye);

    var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
    var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, (float)ViewW / ViewH, 1f, 20000f);
    var worldToProjection = view * proj;

    var sphereCentre = new Vector3(10f, 60f, 30f);
    var sphereRadius = 110f;

    var feeder = new TiledCullFeeder();
    feeder.Begin(Cols, Rows, TileSize, DepthBins, KeyRange, new Vector2(ViewW, ViewH),
        worldToProjection, eye, forward, 1f);

    var volume = Matrix4x4.CreateScale(4000f) * Matrix4x4.CreateTranslation(sphereCentre);
    feeder.AddBarnLights(new[]
    {
        new BarnLightCullVolume
        {
            FrustumToWorld = volume,
            RangeSphere = new Vector4(sphereCentre, sphereRadius),
        },
    });
    feeder.End();

    var item = feeder.ItemArray[0];

    if (item.ConicEnable == 0f)
    {
        Console.WriteLine("  FAIL conic was not produced");
        failures++;
    }
    else
    {
        Matrix4x4.Invert(worldToProjection, out var projectionToWorld);

        Vector3 Unproject(Vector2 ndc, float z)
        {
            var h = Vector4.Transform(new Vector4(ndc.X, ndc.Y, z, 1f), projectionToWorld);
            return new Vector3(h.X, h.Y, h.Z) / h.W;
        }

        var atCentre = Vector4.Transform(new Vector4(sphereCentre, 1f), worldToProjection);
        var axis = Vector3.Normalize(Vector3.Cross(sphereCentre - eye, forward)) * sphereRadius;
        var atEdge = Vector4.Transform(new Vector4(sphereCentre + axis, 1f), worldToProjection);
        var pc = ((new Vector2(atCentre.X, atCentre.Y) / atCentre.W) * 0.5f + new Vector2(0.5f)) * new Vector2(ViewW, ViewH);
        var pe = ((new Vector2(atEdge.X, atEdge.Y) / atEdge.W) * 0.5f + new Vector2(0.5f)) * new Vector2(ViewW, ViewH);
        var dilated = sphereRadius + (TileSize * 0.5f * MathF.Sqrt(2f) * (sphereRadius / (pe - pc).Length()));

        var checkedPixels = 0;
        var conicInside = 0;

        for (var py = 0; py < ViewH; py += 2)
        {
            for (var px = 0; px < ViewW; px += 2)
            {
                var pixel = new Vector2(px + 0.5f, py + 0.5f);
                var ndc = (pixel / new Vector2(ViewW, ViewH) * 2f) - Vector2.One;

                var dir = Vector3.Normalize(Unproject(ndc, 0.9f) - Unproject(ndc, 0.1f));
                var c = sphereCentre - eye;

                var along = Vector3.Dot(dir, c);
                var perpSq = c.LengthSquared() - (along * along);
                var reference = perpSq <= dilated * dilated;

                var x = pixel.X;
                var y = pixel.Y;
                var value = (item.ConicXX * x * x) + (item.ConicYY * y * y) + (item.ConicXY * x * y)
                          + (item.ConicX * x) + (item.ConicY * y) + item.ConicConst;

                if (MathF.Abs(MathF.Sqrt(MathF.Max(perpSq, 0f)) - dilated) < 1.5f)
                {
                    continue;
                }

                checkedPixels++;
                if (value >= 0f) conicInside++;

                Check($"conic at pixel ({px},{py}) expected inside={reference} got {value >= 0f}",
                    reference == (value >= 0f));
            }
        }

        Console.WriteLine($"conic: {checkedPixels} pixels checked, {conicInside} inside");
    }
}

{
    var eye = new Vector3(0f, -520f, 180f);
    var forward = Vector3.Normalize(new Vector3(0f, 40f, 0f) - eye);

    var view = Matrix4x4.CreateLookAt(eye, eye + forward, Vector3.UnitZ);
    var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, (float)ViewW / ViewH, 1f, 20000f);
    var worldToProjection = view * proj;

    var frustum = Matrix4x4.CreateScale(600f) * Matrix4x4.CreateTranslation(new Vector3(0f, 0f, -200f));
    var obb = Matrix4x4.CreateScale(220f) * Matrix4x4.CreateTranslation(new Vector3(-30f, 20f, 40f));

    CullItem Build(Matrix4x4 obbToWorld, TiledCullFeeder feeder)
    {
        feeder.Begin(Cols, Rows, TileSize, DepthBins, KeyRange, new Vector2(ViewW, ViewH),
            worldToProjection, eye, forward, 1f);
        feeder.AddBarnLights(new[]
        {
            new BarnLightCullVolume { FrustumToWorld = frustum, ObbToWorld = obbToWorld },
        });
        feeder.End();

        return feeder.ItemArray[0];
    }

    var withObb = Build(obb, new TiledCullFeeder());
    var feederNoObb = new TiledCullFeeder();
    var withoutObb = Build(default, feederNoObb);

    Check("a face with no OBB emits no second hull", withoutObb.NumPlanes1 == 0u);
    Check("a face with an OBB emits a second hull", withObb.NumPlanes1 != 0u);
    Check("the OBB shrinks the item's bounds",
        withObb.BoundsMin.X > withoutObb.BoundsMin.X && withObb.BoundsMax.X < withoutObb.BoundsMax.X);
    Check("the OBB shrinks the item's depth range",
        withObb.DepthMin > withoutObb.DepthMin && withObb.DepthMax < withoutObb.DepthMax);

    if (withObb.NumPlanes1 != 0u)
    {
        var feeder = new TiledCullFeeder();
        var item = Build(obb, feeder);

        var hull1 = new Vector2[item.NumPlanes1];
        Array.Copy(feeder.PlaneArray, (int)(item.FirstPlane + item.NumPlanes0), hull1, 0, hull1.Length);

        var points = new Vector2[8];

        for (var corner = 0; corner < 8; corner++)
        {
            var cube = new Vector4(
                (corner & 1) != 0 ? 1f : -1f,
                (corner & 2) != 0 ? 1f : -1f,
                (corner & 4) != 0 ? 1f : -1f,
                1f);

            var world = Vector4.Transform(cube, obb);
            var clip = Vector4.Transform(new Vector4(world.X, world.Y, world.Z, 1f), worldToProjection);
            points[corner] = ((new Vector2(clip.X, clip.Y) / clip.W) * 0.5f + new Vector2(0.5f))
                             * new Vector2(ViewW, ViewH);
        }

        var reference = new List<Vector2>();
        var start = 0;

        for (var i = 1; i < points.Length; i++)
        {
            if (points[i].X < points[start].X || (points[i].X == points[start].X && points[i].Y < points[start].Y))
            {
                start = i;
            }
        }

        var onHull = start;

        do
        {
            reference.Add(points[onHull]);
            var next = (onHull + 1) % points.Length;

            for (var i = 0; i < points.Length; i++)
            {
                var a = points[next] - points[onHull];
                var b = points[i] - points[onHull];

                if ((a.X * b.Y) - (a.Y * b.X) < 0f)
                {
                    next = i;
                }
            }

            onHull = next;
        }
        while (onHull != start && reference.Count <= points.Length);

        var area = 0f;

        for (var i = 0; i < reference.Count; i++)
        {
            var prev = reference[(i + reference.Count - 1) % reference.Count];
            area += (prev.X * reference[i].Y) - (reference[i].X * prev.Y);
        }

        if (area < 0f)
        {
            reference.Reverse();
        }

        var polygon = reference.ToArray();
        var band = TileSize * MathF.Sqrt(2f);
        var tested = 0;
        var marked = 0;

        for (var ty = 0; ty < Rows; ty++)
        {
            for (var tx = 0; tx < Cols; tx++)
            {
                var centre = new Vector2((tx + 0.5f) * TileSize, (ty + 0.5f) * TileSize);

                var nearest = float.MaxValue;

                for (var i = 0; i < polygon.Length; i++)
                {
                    var prev = polygon[(i + polygon.Length - 1) % polygon.Length];
                    var edge = polygon[i] - prev;
                    var normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));

                    nearest = MathF.Min(nearest, Vector2.Dot(normal, centre - polygon[i]));
                }

                if (MathF.Abs(nearest) < band)
                {
                    continue;
                }

                tested++;
                var actual = InsideHull(centre, hull1);

                if (actual)
                {
                    marked++;
                }

                Check($"second hull at tile ({tx},{ty}) expected inside={nearest > 0f} got {actual}",
                    actual == nearest > 0f);
            }
        }

        Check("the second hull marks something", marked > 0);
        Console.WriteLine($"second hull: {tested} tiles checked, {marked} inside");
    }

    var disjoint = Build(Matrix4x4.CreateScale(90f) * Matrix4x4.CreateTranslation(new Vector3(9000f, 0f, 0f)),
        new TiledCullFeeder());

    Check("an OBB clear of the frustum rejects the item",
        disjoint.NumPlanes0 == 0u && disjoint.BoundsMin.X > disjoint.BoundsMax.X);
}

{
    var viewport = new Vector2(1920f, 1080f);
    var worst = 0f;
    var points = 0;

    foreach (var mainFov in new[] { 60f, 75f, 90f, 106f })
    {
        foreach (var viewmodelSetting in new[] { 40f, 64f, 80f })
        {
            var main = new Camera();
            main.SetViewportSize((int)viewport.X, (int)viewport.Y);
            main.SetLocationPitchYaw(new Vector3(120f, -40f, 65f), 0.31f, -1.1f);
            main.FieldOfView = mainFov;
            main.CreateProjectionMatrix();
            main.RecalculateMatrices();

            var viewmodel = new Camera();
            viewmodel.CopyFrom(main);
            viewmodel.FieldOfView = viewmodelSetting * (mainFov / 90f);
            viewmodel.CreateProjectionMatrix();
            viewmodel.RecalculateMatrices();

            var remap = viewmodel.GetPixelRemapTo(main, viewport);
            Matrix4x4.Invert(viewmodel.ViewProjectionMatrix, out var viewmodelInverse);

            for (var i = 0; i < 400; i++)
            {
                var ndc = new Vector2((((i * 37) % 41) / 20f) - 1f, (((i * 53) % 29) / 14f) - 1f);
                var distance = 4f + ((i % 17) * 30f);

                var h = Vector4.Transform(new Vector4(ndc.X, ndc.Y, 1f / (1f + distance), 1f), viewmodelInverse);
                var world = new Vector3(h.X, h.Y, h.Z) / h.W;

                var drawn = Vector4.Transform(new Vector4(world, 1f), viewmodel.ViewProjectionMatrix);
                var culled = Vector4.Transform(new Vector4(world, 1f), main.ViewProjectionMatrix);

                if (drawn.W <= 1e-4f || culled.W <= 1e-4f)
                {
                    continue;
                }

                var drawnPixel = (((new Vector2(drawn.X, drawn.Y) / drawn.W) * 0.5f) + new Vector2(0.5f)) * viewport;
                var reference = (((new Vector2(culled.X, culled.Y) / culled.W) * 0.5f) + new Vector2(0.5f)) * viewport;

                var remapped = new Vector2((drawnPixel.X * remap.X) + remap.Z, (drawnPixel.Y * remap.Y) + remap.W);

                worst = MathF.Max(worst, (remapped - reference).Length());
                points++;
            }

            Check($"remap at fov {mainFov}/{viewmodelSetting} contracts", remap.X < 1f && remap.Y < 1f);
        }
    }

    Check($"pixel remap matches reprojection, worst {worst:F4} px", worst < 0.01f);
    Console.WriteLine($"pixel remap: {points} points, worst disagreement {worst:F6} px");
}

// ---------------------------------------------------------------------------------------------------
// Partial edge row. A viewport that is not a whole number of tiles leaves a row whose tile is wider than
// the pixels it owns, and when the leftover is under half a tile that tile's centre sits past the last
// pixel. Hulls are dilated by half a tile, so an item whose silhouette clears the screen by less than
// that still tests inside there and lights the sliver of the row that is on screen. An item is kept
// exactly while its silhouette touches the viewport, which is what this walks one pixel at a time.

{
    const int TileSizeE = 16;
    const int ViewWE = 256;

    foreach (var viewHE in new[] { 128, 120, 122 })
    {
        var colsE = (ViewWE + TileSizeE - 1) / TileSizeE;
        var rowsE = (viewHE + TileSizeE - 1) / TileSizeE;

        var eye = new Vector3(0f, -400f, 0f);
        var forward = new Vector3(0f, 1f, 0f);
        var worldToProjection = Matrix4x4.CreateLookAt(eye, eye + forward, Vector3.UnitZ)
            * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, (float)ViewWE / viewHE, 1f, 20000f);

        var feeder = new TiledCullFeeder();
        var kept = 0;
        var dropped = 0;

        for (var step = 0; step < 400; step++)
        {
            var height = 300f + (step * 1.5f);
            var volume = Matrix4x4.CreateScale(20f) * Matrix4x4.CreateTranslation(new Vector3(0f, 400f, height));

            feeder.Begin(colsE, rowsE, TileSizeE, DepthBins, KeyRange, new Vector2(ViewWE, viewHE),
                worldToProjection, eye, forward, 1f);
            feeder.AddBarnLights(new[] { new BarnLightCullVolume { FrustumToWorld = volume } });
            feeder.End();

            // The silhouette, projected here rather than taken from the item, whose bounds are dilated.
            var rawMin = new Vector2(float.MaxValue);
            var rawMax = new Vector2(float.MinValue);

            for (var corner = 0; corner < 8; corner++)
            {
                var clip = new Vector4(
                    (corner & 1) != 0 ? 1f : -1f,
                    (corner & 2) != 0 ? 1f : -1f,
                    (corner & 4) != 0 ? 1f : 0f,
                    1f);

                var world = Vector4.Transform(clip, volume);
                var proj = Vector4.Transform(new Vector4(world.X, world.Y, world.Z, 1f) / world.W, worldToProjection);
                var pixel = (((new Vector2(proj.X, proj.Y) / proj.W) * 0.5f) + new Vector2(0.5f))
                            * new Vector2(ViewWE, viewHE);

                rawMin = Vector2.Min(rawMin, pixel);
                rawMax = Vector2.Max(rawMax, pixel);
            }

            var onScreen = rawMax.Y >= 0f && rawMin.Y <= viewHE;
            var item = feeder.ItemArray[0];

            // Skip the pixel the two sides can disagree on by rounding alone.
            if (MathF.Abs(rawMin.Y - viewHE) < 1f)
            {
                continue;
            }

            if (onScreen) { kept++; } else { dropped++; }

            Check($"H={viewHE} silhouette top {rawMin.Y:F1} onScreen={onScreen} but kept={item.NumPlanes0 != 0u}",
                onScreen == (item.NumPlanes0 != 0u));
        }

        Console.WriteLine($"partial row H={viewHE} rows={rowsE} partial={viewHE % TileSizeE != 0}: "
            + $"{kept} kept, {dropped} dropped");
    }
}

// ---------------------------------------------------------------------------------------------------
// End to end at a viewport that is not a whole number of tiles. Everything above tests one side or the
// other; this drives the feeder into the real dispatch and checks every tile of every batch against the
// same point in hull test the shader is specified to do, so a disagreement anywhere between projecting a
// volume and reading a bit back shows up here.

foreach (var (viewW2, viewH2) in new[] { (256, 128), (250, 122), (247, 119), (241, 113) })
{
    var cols2 = (viewW2 + TileSize - 1) / TileSize;
    var rows2 = (viewH2 + TileSize - 1) / TileSize;

    var eye2 = new Vector3(0f, -400f, 0f);
    var fwd2 = new Vector3(0f, 1f, 0f);
    var wtp2 = Matrix4x4.CreateLookAt(eye2, eye2 + fwd2, Vector3.UnitZ)
        * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, (float)viewW2 / viewH2, 1f, 20000f);

    var feeder2 = new TiledCullFeeder();
    feeder2.Begin(cols2, rows2, TileSize, DepthBins, KeyRange, new Vector2(viewW2, viewH2),
        wtp2, eye2, fwd2, 1f);

    // One volume that swallows the whole view, so every tile must be set.
    feeder2.AddBarnLights(new[] { new BarnLightCullVolume { FrustumToWorld = Matrix4x4.CreateScale(4000f) } });
    feeder2.AddEnvMaps([new ValveResourceFormat.Renderer.SceneEnvironment.SceneEnvMap(null!, new AABB(new Vector3(-4000f), new Vector3(4000f)))
        { EnvMapTexture = null!, ShaderIndex = 0 }]);
    feeder2.End();

    var bits2 = new uint[feeder2.TotalWords];
    for (var i = 0; i < bits2.Length; i++) { bits2[i] = 0xDEADBEEF; }

    var bitsBuffer2 = MakeSsbo(13, bits2, bits2.Length);
    MakeSsbo(14, feeder2.ItemArray, feeder2.ItemArray.Length);
    MakeSsbo(15, feeder2.PlaneArray, feeder2.PlaneArray.Length);

    var params2 = feeder2.Params;
    GL.CreateBuffers(1, out int paramsBuffer2);
    GL.NamedBufferData(paramsBuffer2, Unsafe.SizeOf<CullParams>(), ref params2, BufferUsageHint.DynamicDraw);
    GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 5, paramsBuffer2);

    tileShader.Use();
    var (dx2, dy2, dz2) = feeder2.TileDispatch;
    GL.DispatchCompute(dx2, dy2, dz2);
    GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
    GL.Finish();
    GL.GetNamedBufferSubData(bitsBuffer2, IntPtr.Zero, bits2.Length * sizeof(uint), bits2);

    foreach (var batch in new[] { TiledCullFeeder.BatchBarnLights, TiledCullFeeder.BatchEnvMaps })
    {
        var item = feeder2.ItemArray[batch == TiledCullFeeder.BatchBarnLights ? 0 : 32];
        var hull2 = new Vector2[item.NumPlanes0];
        Array.Copy(feeder2.PlaneArray, (int)item.FirstPlane, hull2, 0, hull2.Length);

        var stride2 = feeder2.Stride(batch);
        var base2 = feeder2.TileBase(batch);
        var missingTop = 0;
        var missingRight = 0;

        for (var ty = 0; ty < rows2; ty++)
        {
            for (var tx = 0; tx < cols2; tx++)
            {
                var centre = new Vector2((tx * TileSize) + (TileSize * 0.5f), (ty * TileSize) + (TileSize * 0.5f));
                var expected = hull2.Length > 0 && InsideHull(centre, hull2);
                var actual = (bits2[base2 + ((uint)((ty * cols2) + tx) * stride2)] & 1u) != 0;

                if (expected != actual)
                {
                    if (ty == rows2 - 1) { missingTop++; }
                    if (tx == cols2 - 1) { missingRight++; }
                }

                Check($"{viewW2}x{viewH2} batch {batch} tile ({tx},{ty}) expected {expected} got {actual}",
                    expected == actual);
            }
        }

        Console.WriteLine($"e2e {viewW2}x{viewH2} batch {batch}: hull {hull2.Length} verts, "
            + $"top row mismatches {missingTop}, right column mismatches {missingRight}");
    }
}

// ---------------------------------------------------------------------------------------------------
// Sweep a small volume across the top edge, end to end. The tile a partial row owns is wider than the
// pixels it covers, so what matters is whether a volume visible in that sliver is still marked there.

{
    const int viewW3 = 250, viewH3 = 122;
    var cols3 = (viewW3 + TileSize - 1) / TileSize;
    var rows3 = (viewH3 + TileSize - 1) / TileSize;

    var eye3 = new Vector3(0f, -400f, 0f);
    var fwd3 = new Vector3(0f, 1f, 0f);
    var wtp3 = Matrix4x4.CreateLookAt(eye3, eye3 + fwd3, Vector3.UnitZ)
        * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, (float)viewW3 / viewH3, 1f, 20000f);

    var worst = 0;

    for (var step = 0; step < 120; step++)
    {
        var height = 150f + (step * 2f);
        // Frustum far larger than the range sphere, so the sphere is the volume and the conic is what
        // decides every tile. This is the omni2 shape: face frustums whose corners stick out past the range.
        var volume = Matrix4x4.CreateScale(200f) * Matrix4x4.CreateTranslation(new Vector3(0f, 400f, height));
        var sphere = new Vector4(0f, 400f, height, 30f);

        var feeder3 = new TiledCullFeeder();
        feeder3.Begin(cols3, rows3, TileSize, DepthBins, KeyRange, new Vector2(viewW3, viewH3),
            wtp3, eye3, fwd3, 1f);
        feeder3.AddBarnLights(new[] { new BarnLightCullVolume { FrustumToWorld = volume, RangeSphere = sphere } });
        feeder3.End();

        var bits3 = new uint[feeder3.TotalWords];
        var buf3 = MakeSsbo(13, bits3, bits3.Length);
        MakeSsbo(14, feeder3.ItemArray, feeder3.ItemArray.Length);
        MakeSsbo(15, feeder3.PlaneArray, feeder3.PlaneArray.Length);

        var p3 = feeder3.Params;
        GL.CreateBuffers(1, out int pb3);
        GL.NamedBufferData(pb3, Unsafe.SizeOf<CullParams>(), ref p3, BufferUsageHint.DynamicDraw);
        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 5, pb3);

        tileShader.Use();
        var (ax, ay, az) = feeder3.TileDispatch;
        GL.DispatchCompute(ax, ay, az);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        GL.Finish();
        GL.GetNamedBufferSubData(buf3, IntPtr.Zero, bits3.Length * sizeof(uint), bits3);

        // The sphere's screen disc, measured the same way the feeder measures it.
        var centreWs = new Vector3(sphere.X, sphere.Y, sphere.Z);
        var axis = Vector3.Normalize(Vector3.Cross(centreWs - eye3, fwd3)) * sphere.W;
        var atCentre = Vector4.Transform(new Vector4(centreWs, 1f), wtp3);
        var atEdge = Vector4.Transform(new Vector4(centreWs + axis, 1f), wtp3);
        var pc = (((new Vector2(atCentre.X, atCentre.Y) / atCentre.W) * 0.5f) + new Vector2(0.5f))
                 * new Vector2(viewW3, viewH3);
        var pe = (((new Vector2(atEdge.X, atEdge.Y) / atEdge.W) * 0.5f) + new Vector2(0.5f))
                 * new Vector2(viewW3, viewH3);
        var discRadius = (pe - pc).Length();

        var rawMin = pc - new Vector2(discRadius);
        var rawMax = pc + new Vector2(discRadius);

        var topRow = rows3 - 1;
        var rowPixelMin = topRow * TileSize;

        // Visible part of the top row, and whether the silhouette covers any of it.
        var coversVisible = rawMax.Y >= rowPixelMin && rawMin.Y <= viewH3 - 1
                         && rawMax.X >= 0f && rawMin.X <= viewW3 - 1;

        if (!coversVisible)
        {
            continue;
        }

        var stride3 = feeder3.Stride(TiledCullFeeder.BatchBarnLights);
        var base3 = feeder3.TileBase(TiledCullFeeder.BatchBarnLights);
        var marked = 0;

        for (var tx = 0; tx < cols3; tx++)
        {
            var centreX = (tx * TileSize) + (TileSize * 0.5f);
            if (centreX < rawMin.X - TileSize || centreX > rawMax.X + TileSize) { continue; }
            if ((bits3[base3 + ((uint)((topRow * cols3) + tx) * stride3)] & 1u) != 0) { marked++; }
        }

        if (marked == 0)
        {
            worst++;
            Check($"silhouette y [{rawMin.Y:F1},{rawMax.Y:F1}] covers the top row sliver "
                + $"[{rowPixelMin},{viewH3 - 1}] but no tile in that row is marked", false);
        }
    }

    Console.WriteLine($"top edge sweep: {worst} positions visible in the top row but unmarked");
}

// ---------------------------------------------------------------------------------------------------
// Every viewport height in a range, with a volume that swallows the view, so every tile must be marked.
// Any height where the edge tiles come back empty is a grid geometry the pass gets wrong.

{
    var bad = new List<string>();
    var tested = 0;

    foreach (var viewW4 in new[] { 250, 241, 242, 248, 256, 129, 130 })
    for (var viewH4 = 100; viewH4 <= 320; viewH4++)
    {
        tested++;
        var cols4 = (viewW4 + TileSize - 1) / TileSize;
        var rows4 = (viewH4 + TileSize - 1) / TileSize;

        var eye4 = new Vector3(0f, -400f, 0f);
        var fwd4 = new Vector3(0f, 1f, 0f);
        var wtp4 = Matrix4x4.CreateLookAt(eye4, eye4 + fwd4, Vector3.UnitZ)
            * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, (float)viewW4 / viewH4, 1f, 20000f);

        var feeder4 = new TiledCullFeeder();
        feeder4.Begin(cols4, rows4, TileSize, DepthBins, KeyRange, new Vector2(viewW4, viewH4),
            wtp4, eye4, fwd4, 1f);
        // Clip z maps to [0,1], so the box has to be pushed back to actually swallow the eye.
        var big4 = Matrix4x4.CreateScale(4000f) * Matrix4x4.CreateTranslation(new Vector3(0f, -400f, -2000f));
        feeder4.AddBarnLights(new[] { new BarnLightCullVolume { FrustumToWorld = big4 } });
        feeder4.End();

        var bits4 = new uint[feeder4.TotalWords];
        var buf4 = MakeSsbo(13, bits4, bits4.Length);
        MakeSsbo(14, feeder4.ItemArray, feeder4.ItemArray.Length);
        MakeSsbo(15, feeder4.PlaneArray, feeder4.PlaneArray.Length);

        var p4 = feeder4.Params;
        GL.CreateBuffers(1, out int pb4);
        GL.NamedBufferData(pb4, Unsafe.SizeOf<CullParams>(), ref p4, BufferUsageHint.DynamicDraw);
        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 5, pb4);

        tileShader.Use();
        var (bx, by, bz) = feeder4.TileDispatch;
        GL.DispatchCompute(bx, by, bz);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        GL.Finish();
        GL.GetNamedBufferSubData(buf4, IntPtr.Zero, bits4.Length * sizeof(uint), bits4);

        var stride4 = feeder4.Stride(TiledCullFeeder.BatchBarnLights);
        var base4 = feeder4.TileBase(TiledCullFeeder.BatchBarnLights);
        var topMissing = 0;
        var rightMissing = 0;
        var anyMissing = 0;

        for (var ty = 0; ty < rows4; ty++)
        {
            for (var tx = 0; tx < cols4; tx++)
            {
                if ((bits4[base4 + ((uint)((ty * cols4) + tx) * stride4)] & 1u) != 0) { continue; }

                anyMissing++;
                if (ty == rows4 - 1) { topMissing++; }
                if (tx == cols4 - 1) { rightMissing++; }
            }
        }

        if (anyMissing > 0)
        {
            bad.Add($"H={viewH4} (rows={rows4}, H%16={viewH4 % TileSize}) missing {anyMissing} "
                + $"[top {topMissing}/{cols4}, right {rightMissing}/{rows4}]");
        }
    }

    foreach (var line in bad)
    {
        Console.WriteLine($"  MISSING {line}");
    }

    Console.WriteLine($"grid sweep: {bad.Count} of {tested} viewport sizes have unmarked tiles");
}

Check("no word left unwritten", Array.IndexOf(bits, 0xDEADBEEF) < 0);

Console.WriteLine(failures == 0 ? $"\nAll checks passed ({totalWords} words)." : $"\n{failures} FAILURES");
return failures == 0 ? 0 : 1;
