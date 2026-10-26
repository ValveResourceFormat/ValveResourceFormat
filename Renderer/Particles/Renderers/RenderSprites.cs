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
            CreateVertexArray(InstanceLayout<CardInstance>(), instanceDivisor: 1);

            // Older definitions play their sheets by age whatever their animation type asks, while the
            // rate is still read by that type
            if (parse.BehaviorVersion < 10)
            {
                AgeAnimationType = ParticleAnimationType.ANIMATION_TYPE_FIXED_RATE;
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

            // TODO: m_bParticleShadows and m_flShadowDensity, where sprites are shaded by the particles between
            // them and the light, are not drawn

            outline = parse.Boolean("m_bOutline", outline);

            if (outline)
            {
                var color = ColorSpace.SrgbGammaToLinear(parse.Color24("m_OutlineColor", new Vector3(1f)));
                outlineColor = new Vector4(color, parse.Int32("m_nOutlineAlpha", 255) / 255f);
                outlineRanges = new Vector4(
                    parse.Float("m_flOutlineStart0", outlineRanges.X),
                    parse.Float("m_flOutlineEnd0", outlineRanges.Y),
                    parse.Float("m_flOutlineStart1", outlineRanges.Z),
                    parse.Float("m_flOutlineEnd1", outlineRanges.W));
            }
        }

        /// <summary>One particle's attributes, which the shader expands into the four corners of its card.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct CardInstance
        {
            /// <summary>The card's centre.</summary>
            [VertexAttribute(VertexSlot.Position)] public Vector3 Position;

            /// <summary>The card's axes per unit of radius, with its roll and yaw turned in.</summary>
            [VertexAttribute("vAxisU")] public Vector3 AxisU;

            /// <inheritdoc cref="AxisU"/>
            [VertexAttribute("vAxisV")] public Vector3 AxisV;

            /// <summary>The colour, its alpha before any fade.</summary>
            [VertexAttribute(VertexSlot.Color)] public Vector4 Color;

            /// <inheritdoc cref="SpritecardVertex.SheetInputs"/>
            [VertexAttribute(VertexSlot.TexCoord)] public Vector4 SheetInputs;

            /// <summary>The sequence the first layer plays, then the one later layers play.</summary>
            [VertexAttribute(VertexSlot.TexCoord1)] public Vector2 Sequences;

            /// <inheritdoc cref="SpritecardVertex.RotationRadius"/>
            [VertexAttribute(VertexSlot.TexCoord3)] public Vector4 RotationRadius;

            /// <inheritdoc cref="SpritecardVertex.FacingAxis"/>
            [VertexAttribute(VertexSlot.Normal)] public Vector3 FacingAxis;
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

        private bool NormalAligned => orientationType is ParticleOrientation.PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL
            or ParticleOrientation.PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL;

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

            var overrideSequence = OverrideSequence(sequenceOverride.NextNumber(systemState));
            var zeroYaw = NormalAligned && !useYawWithNormalAligned;

            var extentScale = screenSpace && orientationType == ParticleOrientation.PARTICLE_ORIENTATION_SCREEN_ALIGNED
                ? ParticleScreenSpaceView.GetScreenAlignedExtentScale(viewCamera.WindowSize)
                : Vector2.One;

            var sorted = SortsByDistance && particles.Count > 1;

            using (var vertexBuffer = new RentedBuffer<CardInstance>(particles.Count))
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
                var extraData1 = current.ReadFloats(ParticleField.ShaderExtraData1);
                var extraData2 = current.ReadFloats(ParticleField.ShaderExtraData2);
                var sequenceNumbers = current.ReadFloats(ParticleField.SequenceNumber);
                var secondSequenceNumbers = current.ReadFloats(ParticleField.SecondSequenceNumber);
                var sheetAges = ReadSheetAges(current);
                var randoms = ReadParticleRandoms(current, systemState);

                for (var n = 0; n < current.Length; n++)
                {
                    var index = sorted ? (int)(uint)order[n] : current.Length - 1 - n;

                    // Each tint channel is packed from the low byte of round(tint * 255) without a clamp, so a tint
                    // past 1 or below 0 wraps; it is saturated here
                    var color = BlendColor(colors[index], alphas[index], alpha2s[index], scales);

                    // The fades only take alpha away, and the shader collapses a card left below 1/255
                    if (color.W < 1f / 255f)
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

                    var radius = radii[index] * scales.Radius;

                    var sequences = overrideSequence >= 0
                        ? new Vector2(overrideSequence)
                        : new Vector2(
                            SequenceLane(sequenceNumbers[index], sequenceNumbersAreRawIndices),
                            SequenceLane(secondSequenceNumbers[index], sequenceNumbersAreRawIndices));

                    // The per particle modes read the yaw as the card turns by it
                    instances[i++] = new CardInstance
                    {
                        Position = BiasTowardCamera(positions[index], viewCamera.Location),
                        AxisU = right * extentScale.X,
                        AxisV = up * extentScale.Y,
                        Color = color,
                        SheetInputs = new Vector4(randoms[index], extraData1[index], sheetAges[index], extraData2[index]),
                        Sequences = sequences,
                        RotationRadius = new Vector4(roll, yaw, pitches[index], radius),
                        FacingAxis = normals[index],
                    };
                }

                UploadVertices(vertexBuffer.ByteArray, i);

                return i;
            }
        }

        /// <inheritdoc/>
        protected override void SetDrawUniforms(ParticleSystemState systemState)
        {
            // TODO: Which flag gates the size clamp and the distance fade of a card is not known. A
            // screen-space card keeps its raw radius, neither faded nor clamped, so a negative one draws mirrored
            var screenSpace = ScreenSpaceCamera != null;

            SetSizeClampAndFadeUniforms(!screenSpace,
                minSize.NextNumber(systemState),
                startFadeSize.NextNumber(systemState),
                endFadeSize.NextNumber(systemState),
                maxSize.NextNumber(systemState));

            // Only the two normal-aligned modes fade by view angle
            SetViewAngleFadeUniforms(NormalAligned && FadesByViewAngle);

            Shader.SetUniform2("uCenterOffset", new Vector2(
                CenterXOffset.NextNumber(systemState),
                CenterYOffset.NextNumber(systemState)));

            Shader.SetUniform1("uOutline", outline);
            Shader.SetUniform4("uOutlineColor", outlineColor);
            Shader.SetUniform4("uOutlineRanges", outlineRanges);

            // Distance alpha with soft edges renders an SDF texture through a smoothstep over the edge softness pair
            var edgeSoftness = distanceAlpha && softEdges
                ? new Vector2(edgeSoftnessEnd, edgeSoftnessStart)
                : NoEdgeSoftness;
            Shader.SetUniform2("uEdgeSoftness", edgeSoftness);
        }
    }
}
