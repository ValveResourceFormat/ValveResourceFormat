using System.Runtime.InteropServices;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders particles as camera-facing or orientation-aligned textured quads (sprites),
    /// with support for sprite sheet animation, blend modes, and per-particle color and alpha.
    /// </summary>
    /// <remarks>
    /// The workhorse renderer used by most effects. Multi-frame sequences can be animated or
    /// used to provide visual variation.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderSprites">C_OP_RenderSprites</seealso>
    internal class RenderSprites : ParticleSpritecardRenderer
    {
        private const string DefaultTextureName = "materials/particle/base_sprite.vtex";

        private readonly INumberProvider minSize = new LiteralNumberProvider(0f);
        private readonly INumberProvider maxSize = new LiteralNumberProvider(5000f);
        private readonly INumberProvider startFadeSize = new LiteralNumberProvider(100000000f);
        private readonly INumberProvider endFadeSize = new LiteralNumberProvider(200000000f);
        /// <summary>
        /// Selects the signed distance field alpha treatment, which drives soft edges and outlines.
        /// It has no bearing on the size clamp or the distance fade.
        /// </summary>
        private readonly bool distanceAlpha;
        private readonly bool softEdges;
        private readonly float edgeSoftnessStart = 0.6f;
        private readonly float edgeSoftnessEnd = 0.5f;

        private readonly ParticleOrientation orientationType;
        private readonly bool useYawWithNormalAligned;
        private readonly INumberProvider sequenceOverride = new LiteralNumberProvider(-1f);
        private readonly bool sequenceNumbersAreRawIndices;

        private readonly bool outline;
        private readonly Vector4 outlineColor = Vector4.One;
        // Start0, End0, Start1, End1 -- the order the shader's two-sided ramp wants them in.
        private readonly Vector4 outlineRanges = new(0.5f, 0.7f, 0.6f, 0.8f);

        public RenderSprites(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene)
            : base(parse, rendererContext, scene, DefaultTextureName, instancing: 1)
        {
            // In buffer order. The axes are three components, so the shader's unwritten w reads as the
            // 1.0 GL fills in, which is the half width a card wants at both of its ends.
            CreateVertexArray(BuildInstanceLayout<CardInstance>(
            [
                new(VertexSlot.Position, DXGI_FORMAT.R32G32B32_FLOAT),
                new("vAxisU", DXGI_FORMAT.R32G32B32_FLOAT),
                new("vAxisV", DXGI_FORMAT.R32G32B32_FLOAT),
                new(VertexSlot.Color, DXGI_FORMAT.R32G32B32A32_FLOAT),
                new(VertexSlot.TexCoord, DXGI_FORMAT.R32G32B32A32_FLOAT),
                new(VertexSlot.TexCoord1, DXGI_FORMAT.R32G32B32A32_FLOAT),
                new("vFrameBlendAlphaRef", DXGI_FORMAT.R32G32B32_FLOAT),
            ]), instanceDivisor: 1);

            if (parse.BehaviorVersion < 10)
            {
                AnimationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
            }

            orientationType = (ParticleOrientation)Math.Clamp((int)parse.Enum("m_nOrientationType", orientationType),
                (int)ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED, (int)ParticleOrientation.PARTICLE_ORIENTATION_FULL_3AXIS_ROTATION);
            useYawWithNormalAligned = parse.Boolean("m_bUseYawWithNormalAligned", useYawWithNormalAligned);
            sequenceOverride = parse.NumberProvider("m_nSequenceOverride", sequenceOverride);
            sequenceNumbersAreRawIndices = parse.Boolean("m_bSequenceNumbersAreRawSequenceIndices", sequenceNumbersAreRawIndices);
            minSize = parse.NumberProvider("m_flMinSize", minSize);
            maxSize = parse.NumberProvider("m_flMaxSize", maxSize);
            startFadeSize = parse.NumberProvider("m_flStartFadeSize", startFadeSize);
            endFadeSize = parse.NumberProvider("m_flEndFadeSize", endFadeSize);
            distanceAlpha = parse.Boolean("m_bDistanceAlpha", distanceAlpha);
            softEdges = parse.Boolean("m_bSoftEdges", softEdges);
            edgeSoftnessStart = parse.Float("m_flEdgeSoftnessStart", edgeSoftnessStart);
            edgeSoftnessEnd = parse.Float("m_flEdgeSoftnessEnd", edgeSoftnessEnd);

            outline = parse.Boolean("m_bOutline", outline);

            if (outline)
            {
                var color = parse.Color24("m_OutlineColor", new Vector3(1f));
                outlineColor = new Vector4(color, parse.Int32("m_nOutlineAlpha", 255) / 255f);
                outlineRanges = new Vector4(
                    parse.Float("m_flOutlineStart0", outlineRanges.X),
                    parse.Float("m_flOutlineEnd0", outlineRanges.Y),
                    parse.Float("m_flOutlineStart1", outlineRanges.Z),
                    parse.Float("m_flOutlineEnd1", outlineRanges.W));
            }
        }

        /// <summary>One particle's attributes, in buffer order. Must match the layout built in the constructor.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct CardInstance
        {
            public Vector3 Origin;
            public Vector3 AxisU;
            public Vector3 AxisV;
            public Vector4 Color;
            public Vector4 UvRect;
            public Vector4 UvRectNext;
            public Vector3 FrameBlendAlphaReference;
        }

        /// <summary>
        /// The part of a sheet tile its art covers, in card coordinates.
        /// </summary>
        private readonly record struct CropWindow(Vector2 Min, Vector2 Max)
        {
            /// <summary>The window that covers a whole tile, for a sheet that crops nothing away.</summary>
            public static CropWindow Full { get; } = new(Vector2.Zero, Vector2.One);

            /// <summary>The window's extent along each card axis.</summary>
            public Vector2 Size => Max - Min;
        }

        /// <summary>The two sheet frames one layer is blending between, and how far it has crossed.</summary>
        private readonly record struct LayerFrames(
            Texture.SpritesheetData.Sequence.Frame.Image Current,
            Texture.SpritesheetData.Sequence.Frame.Image Next,
            float Blend);

        private static Vector2 Normalize(Vector2 point, Vector2 min, Vector2 size) => new(
            size.X != 0f ? (point.X - min.X) / size.X : 0f,
            size.Y != 0f ? (point.Y - min.Y) / size.Y : 0f);

        /// <summary>The card coordinates a frame's cropped art occupies within its own tile.</summary>
        private static CropWindow TileWindow(Texture.SpritesheetData.Sequence.Frame.Image image)
        {
            var size = image.UncroppedMax - image.UncroppedMin;

            return new CropWindow(
                Normalize(image.CroppedMin, image.UncroppedMin, size),
                Normalize(image.CroppedMax, image.UncroppedMin, size));
        }

        /// <summary>Places a card window back in the atlas, against the tile it was measured in.</summary>
        private static (Vector2 Min, Vector2 Max) WindowRect(Texture.SpritesheetData.Sequence.Frame.Image image, CropWindow window)
        {
            var size = image.UncroppedMax - image.UncroppedMin;

            return (image.UncroppedMin + (window.Min * size), image.UncroppedMin + (window.Max * size));
        }

        /// <summary>
        /// The frames a layer's own sheet is showing, or null when it carries no sequence.
        /// </summary>
        /// <remarks>
        /// Layer 0 plays the particle's sequence, later layers the second sequence (0 unless set), and
        /// a sequence override replaces both. Sequence numbers outside the sheet play sequence 0.
        /// Layer N of a multi-image frame uses image N, and the last image covers any layers beyond that.
        /// </remarks>
        private LayerFrames? GetLayerFrames(int layer, in SheetInputs inputs, int index, int overrideSequence, float rateScale)
        {
            var sequenceNumber = overrideSequence;

            if (overrideSequence < 0)
            {
                var particleSequence = layer > 0 ? inputs.SecondSequenceNumbers[index] : inputs.SequenceNumbers[index];
                sequenceNumber = SequenceIndex(particleSequence);
            }

            if (GetSheetFrames(layer, sequenceNumber, inputs, index, rateScale) is not { } frames)
            {
                return null;
            }

            return new LayerFrames(LayerImage(frames.Current, layer), LayerImage(frames.Next, layer), frames.Blend);
        }

        /// <summary>
        /// The sheet position of the sequence a particle's sequence number names. The number is rounded
        /// to the nearest integer and, unless it is a raw index, read as a sequence id of the first
        /// layer's sheet; an id that sheet does not have plays sequence 0.
        /// </summary>
        private int SequenceIndex(float sequenceNumber)
        {
            var number = (int)MathF.Round(sequenceNumber);
            var sequences = Layers[0].Texture.SpriteSheetData?.Sequences;

            if (sequenceNumbersAreRawIndices || sequences == null || number < 0)
            {
                return number;
            }

            var id = (uint)number;

            if (number < sequences.Length && sequences[number].Id == id)
            {
                return number;
            }

            for (var i = 0; i < sequences.Length; i++)
            {
                if (sequences[i].Id == id)
                {
                    return i;
                }
            }

            return 0;
        }

        private static Texture.SpritesheetData.Sequence.Frame.Image LayerImage(Texture.SpritesheetData.Sequence.Frame frame, int layer)
            => frame.Images[Math.Min(layer, frame.Images.Length - 1)];

        /// <summary>
        /// The window the card shrinks to, covering the art of both frames it is blending so neither is
        /// clipped, and none of the tile's packed neighbours is drawn.
        /// </summary>
        private static CropWindow GetCardCropWindow(LayerFrames? frames)
        {
            if (frames is not { } cardFrames)
            {
                return CropWindow.Full;
            }

            var current = TileWindow(cardFrames.Current);
            var next = TileWindow(cardFrames.Next);

            return new CropWindow(
                Vector2.Min(current.Min, next.Min),
                Vector2.Max(current.Max, next.Max));
        }

        /// <summary>The atlas rectangles a layer draws its two frames from, cropped to the card's window.</summary>
        private static SheetRects GetLayerSheetRects(LayerFrames? frames, CropWindow window)
        {
            if (frames is not { } layerFrames)
            {
                return SheetRects.Whole;
            }

            var (min, max) = WindowRect(layerFrames.Current, window);
            var (nextMin, nextMax) = WindowRect(layerFrames.Next, window);

            return new SheetRects(min, max, nextMin, nextMax);
        }

        // The quad's (right, up) axes from a base pair with the particle roll folded in, matching the
        // spritecard vertex shader. The axes are intentionally not re-normalized (some modes rely on that, e.g.
        // SCREEN_Z foreshortens as the camera tilts).
        private static (Vector3 Right, Vector3 Up) QuadBasis(Vector3 baseRight, Vector3 baseUp, float roll, float yaw)
        {
            var c = MathF.Cos(roll);
            var s = MathF.Sin(roll);
            var right = (baseRight * c) + (baseUp * s);
            var up = (baseUp * c) - (baseRight * s);

            // Yaw turns the card about its own up axis, foreshortening it horizontally to nothing at 90
            // degrees. Only the right axis is turned, and the axis is normalized without touching up.
            if (yaw != 0f && up.LengthSquared() > ParticleMath.MinimumLengthSquared)
            {
                right = Vector3.Transform(right, Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(up), yaw));
            }

            return (right, up);
        }

        private static (Vector3 Right, Vector3 Up) RotationBasis(in Matrix4x4 rotation)
            => (rotation.GetRow(0).AsVector3(), rotation.GetRow(1).AsVector3());

        // World-space camera forward (into the scene): the billboard maps local +Z to the toward-camera axis.
        private static Vector3 CameraForward(in Matrix4x4 billboard)
            => -billboard.GetRow(2).AsVector3();

        // SCREEN_ALIGNED: the plain camera billboard, built from the camera's own right and up axes. The
        // particle's pitch never enters, so a normal-setting operator cannot tilt the card out of plane.
        private static (Vector3 Right, Vector3 Up) ScreenAlignedBasis(in Matrix4x4 billboard, float roll, float yaw)
            => QuadBasis(
                billboard.GetRow(0).AsVector3(),
                billboard.GetRow(1).AsVector3(), roll, yaw);

        // SCREEN_Z_ALIGNED: up locked to world +Z, right = cross(worldZ, forward) left un-normalized, so the
        // sprite yaws about vertical to face the camera and foreshortens as the view tilts off-horizontal.
        private static (Vector3 Right, Vector3 Up) ScreenZAlignedBasis(in Matrix4x4 billboard, float roll, float yaw)
            => QuadBasis(Vector3.Cross(Vector3.UnitZ, CameraForward(billboard)), Vector3.UnitZ, roll, yaw);

        // WORLD_Z_ALIGNED: the quad lies flat in the world XY plane (normal = +Z), rolling about vertical,
        // independent of the camera.
        private static (Vector3 Right, Vector3 Up) WorldZAlignedBasis(float roll, float yaw)
            => QuadBasis(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), roll, yaw);

        // ALIGN_TO_PARTICLE_NORMAL: quad plane perpendicular to the particle normal, with the shader's canonical
        // tangent frame. The reference axis is world -Y once the normal tilts at all off horizontal, and world
        // +Z only while it is nearly horizontal; either choice stays clear of the normal.
        private static (Vector3 Right, Vector3 Up) ParticleNormalBasis(Vector3 normal, float roll, float yaw)
        {
            var reference = MathF.Abs(normal.Z) > 0.1f ? new Vector3(0f, -1f, 0f) : new Vector3(0f, 0f, 1f);
            var up = Vector3.Normalize(Vector3.Cross(normal, reference));
            var right = Vector3.Cross(up, normal);
            return QuadBasis(right, up, roll, yaw);
        }

        // SCREENALIGN_TO_PARTICLE_NORMAL: the quad's right edge follows the particle normal while it turns toward
        // the camera about that normal. Falls back to a billboard when the normal points at the camera.
        private static (Vector3 Right, Vector3 Up) ScreenAlignToNormalBasis(in Matrix4x4 billboard, Vector3 normal, float roll, float yaw)
        {
            var n = Vector3.Normalize(normal);
            var w = Vector3.Cross(n, CameraForward(billboard));
            if (w.LengthSquared() < ParticleMath.MinimumLengthSquared)
            {
                return RotationBasis(billboard);
            }

            return QuadBasis(n, Vector3.Normalize(w), roll, yaw);
        }

        /// <inheritdoc/>
        protected override int UpdateVertices(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            // A screen-space system turns its cards to the screen-space view, while sorting and the
            // visibility inputs keep measuring from the scene camera
            var screenSpace = ScreenSpaceCamera != null;
            var viewCamera = ScreenSpaceCamera ?? camera;
            var billboardMatrix = viewCamera.BillboardMatrix;
            var scales = EvaluateScales(systemState, camera);

            if (scales.Radius == 0f || scales.Alpha == 0f)
            {
                return 0;
            }

            var overrideSequence = (int)MathF.Round(sequenceOverride.NextNumber(systemState));

            Span<float> sheetRateScales = stackalloc float[ParticleTextureLayer.MaxLayers];
            ParticleTextureLayer.ResolveSheetRateScales(Layers, systemState, sheetRateScales);
            var normalAligned = orientationType is ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
                or ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL;
            var zeroYaw = normalAligned && !useYawWithNormalAligned;

            // All four bounds are a radius per unit of camera distance
            var minSizeSlope = minSize.NextNumber(systemState);
            var maxSizeSlope = maxSize.NextNumber(systemState);
            var startFadeSlope = startFadeSize.NextNumber(systemState);
            var endFadeSlope = endFadeSize.NextNumber(systemState);

            var centerOffset = new Vector2(
                CenterXOffset.NextNumber(systemState),
                CenterYOffset.NextNumber(systemState));

            // Only the two normal-aligned modes fade by view angle
            var viewAngleFadeActive = normalAligned && FadesByViewAngle;

            var extentScale = screenSpace && orientationType == ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED
                ? ParticleScreenSpaceView.GetScreenAlignedExtentScale(viewCamera.WindowSize)
                : Vector2.One;

            // One set of attributes per particle, rented from the shared byte pool so the memory is
            // reused across renderers.
            var instanceFloats = VertexFloats;

            var sorted = SortsByDistance && particles.Count > 1;

            using (var vertexBuffer = new RentedBuffer<float>(particles.Count * instanceFloats))
            using (var drawOrder = new RentedBuffer<ulong>(sorted ? particles.Count : 0))
            {
                var instances = vertexBuffer.Span;
                var current = GetBlendedParticles(particles, systemState);
                var i = 0;

                var order = drawOrder.Span;

                if (sorted)
                {
                    SortBackToFront(current, camera.Location, order);
                }

                var positions = current.ReadVectors(ParticleField.Position);
                var normals = current.ReadVectors(ParticleField.Normal);
                var colors = current.ReadVectors(ParticleField.Color);
                var radii = current.ReadFloats(ParticleField.Radius);
                var alphas = current.ReadFloats(ParticleField.Alpha);
                var alpha2s = ReadAlpha2(current);
                var rolls = current.ReadFloats(ParticleField.Roll);
                var yaws = current.ReadFloats(ParticleField.Yaw);
                var pitches = current.ReadFloats(ParticleField.Pitch);
                var sheetInputs = new SheetInputs(current);
                var alphaReferenceInputs = ReadAlphaReferenceInputs(current, sheetInputs.Ages);

                for (var n = 0; n < current.Length; n++)
                {
                    var index = sorted ? (int)(uint)order[n] : current.Length - 1 - n;
                    var radiusScale = scales.Radius;
                    var position = BiasTowardCamera(positions[index], viewCamera.Location);

                    // Scales rgb and alpha alike, matching the shader's fade of the whole vertex colour.
                    var colorFade = 1f;

                    // The view-angle fade touches alpha only, unlike the size fade below.
                    var alphaFade = 1f;

                    if (viewAngleFadeActive)
                    {
                        alphaFade = ViewAngleFade(normals[index], position, viewCamera.Location);
                    }

                    var particleRadius = radii[index];

                    // A screen-space card keeps its raw radius, neither faded nor clamped by its size on screen
                    if (!screenSpace)
                    {
                        var cameraDistance = Vector3.Distance(camera.Location, position);
                        var radius = particleRadius * radiusScale;
                        var fadeStart = startFadeSlope * cameraDistance;
                        var fadeEnd = endFadeSlope * cameraDistance;

                        // The fade reads the raw radius, independently of the size clamp below
                        if (radius > fadeStart)
                        {
                            if (radius >= fadeEnd)
                            {
                                continue;
                            }

                            colorFade = 1f - MathUtils.Remap(radius, fadeStart, fadeEnd);
                        }

                        // Nested min/max rather than a clamp, so an inverted range resolves to the maximum
                        if (particleRadius > 0f)
                        {
                            radiusScale = MathF.Min(MathF.Max(radius, minSizeSlope * cameraDistance), maxSizeSlope * cameraDistance) / particleRadius;
                        }
                    }

                    var color = BlendColor(colors[index], alphas[index], alpha2s[index], scales);
                    var alpha = color.W * colorFade * alphaFade;
                    var halfWidth = particleRadius * radiusScale;

                    // The spritecard vertex shader scales the corner offset to zero below 1/255 alpha, so
                    // a quad with no extent or no alpha rasterizes nothing either way.
                    if (halfWidth <= 0f || alpha < 1f / 255f)
                    {
                        continue;
                    }

                    // Per-mode quad orientation, as in the spritecard vertex shader. Every mode folds in
                    // roll and yaw and none of them read pitch; only FULL_3AXIS_ROTATION uses the full basis.
                    var roll = rolls[index] * scales.Roll;
                    var yaw = zeroYaw ? 0f : yaws[index];
                    var (right, up) = orientationType switch
                    {
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED => ScreenAlignedBasis(billboardMatrix, roll, yaw),
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_Z_ALIGNED => ScreenZAlignedBasis(billboardMatrix, roll, yaw),
                        ParticleOrientation.PARTICLE_ORIENTATION_WORLD_Z_ALIGNED => WorldZAlignedBasis(roll, yaw),
                        ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL => ParticleNormalBasis(normals[index], roll, yaw),
                        ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL => ScreenAlignToNormalBasis(billboardMatrix, normals[index], roll, yaw),
                        _ => RotationBasis(EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(pitches[index], yaw, roll))),
                    };

                    // The corner map is corner.x * right + corner.y * up + origin, so the card's axes carry
                    // the radius.
                    right *= halfWidth * extentScale.X;
                    up *= halfWidth * extentScale.Y;

                    // The centre offset shifts the corners before the radius scales them, so it is
                    // measured in half-widths and folds into the origin along those same two axes.
                    var origin = position
                        + (centerOffset.X * right)
                        + (centerOffset.Y * up);

                    var cardFrames = GetLayerFrames(0, sheetInputs, index, overrideSequence, sheetRateScales[0]);
                    var window = GetCardCropWindow(cardFrames);
                    var windowSize = window.Size;

                    origin += ((window.Max.X + window.Min.X - 1f) * right)
                        + ((1f - window.Min.Y - window.Max.Y) * up);

                    right *= windowSize.X;
                    up *= windowSize.Y;

                    // Each layer resolves frame rects against its own sheet, timed by the base sequence:
                    // companion sheets match the base rects, one-frame sequences pin an atlas region.
                    var sheetRects = GetLayerSheetRects(cardFrames, window);
                    var alphaReference = GetAlphaReference(alphaReferenceInputs, index, halfWidth, alpha);

                    var layerRects = WriteInstance(instances, i, new CardInstance
                    {
                        Origin = origin,
                        AxisU = right,
                        AxisV = up,
                        Color = new Vector4(color.AsVector3() * colorFade, alpha),
                        UvRect = Rect(sheetRects.Min, sheetRects.Max),
                        UvRectNext = Rect(sheetRects.NextMin, sheetRects.NextMax),
                        FrameBlendAlphaReference = new Vector3(cardFrames?.Blend ?? 0f, alphaReference.X, alphaReference.Y),
                    });

                    for (var layer = 1; layer < Layers.Length; layer++)
                    {
                        var layerFrames = GetLayerFrames(layer, sheetInputs, index, overrideSequence, sheetRateScales[layer]);
                        WriteLayerRects(layerRects, layer, GetLayerSheetRects(layerFrames, window));
                    }

                    i++;
                }

                UploadVertices(vertexBuffer.ByteArray, i);

                return i;
            }
        }

        /// <inheritdoc/>
        protected override void SetDrawUniforms(ParticleSystemState systemState)
        {
            ParticleTextureLayer.BindUvTransforms(Shader, Layers, systemState);

            Shader.SetUniform1("uOutline", outline);
            Shader.SetUniform4("uOutlineColor", outlineColor);
            Shader.SetUniform4("uOutlineRanges", outlineRanges);

            // Distance alpha renders an SDF texture through the alpha remap smoothstep: the edge
            // softness pair replaces the remap range, or a hard threshold just under 0.5.
            if (distanceAlpha)
            {
                var remap = softEdges
                    ? new Vector2(edgeSoftnessEnd, edgeSoftnessStart)
                    : new Vector2(GetAlphaRemapRange(systemState).X, 0.499f);
                Shader.SetUniform2("uAlphaRemapRange", remap);
            }
        }
    }
}
