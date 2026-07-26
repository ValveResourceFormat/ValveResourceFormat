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

Check("no word left unwritten", Array.IndexOf(bits, 0xDEADBEEF) < 0);

Console.WriteLine(failures == 0 ? $"\nAll checks passed ({totalWords} words)." : $"\n{failures} FAILURES");
return failures == 0 ? 0 : 1;
