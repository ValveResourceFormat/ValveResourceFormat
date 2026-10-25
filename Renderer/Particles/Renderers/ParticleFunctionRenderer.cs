using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Base class for particle renderers, which draw a particle collection each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionRenderer">CParticleFunctionRenderer</seealso>
    abstract class ParticleFunctionRenderer : ParticleFunction
    {
        /// <summary>Scales the radius every particle is drawn at.</summary>
        protected INumberProvider RadiusScale { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the alpha every particle is drawn at.</summary>
        protected INumberProvider AlphaScale { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the roll every particle is drawn at.</summary>
        protected INumberProvider RollScale { get; } = new LiteralNumberProvider(1f);

        /// <summary>Scales the colour every particle is drawn at, combined as <see cref="ColorBlendType"/> asks.</summary>
        protected IVectorProvider ColorScale { get; } = new LiteralVectorProvider(Vector3.One);

        /// <summary>How <see cref="ColorScale"/> combines with each particle's colour.</summary>
        protected ParticleColorBlendType ColorBlendType { get; }

        /// <summary>The attribute that scales each particle's alpha alongside its own, or <see cref="ParticleField.NoneDisabled"/> for none.</summary>
        protected ParticleField Alpha2Field { get; } = ParticleField.AlphaAlternate;

        /// <summary>
        /// The bit mask over <see cref="ParticleField"/> of the attributes this renderer blends between
        /// two steps besides those every renderer blends: its <see cref="Alpha2Field"/>.
        /// </summary>
        public ulong BlendedFields => ParticleBuffer.IsScalar(Alpha2Field) ? Alpha2Field.Mask() : 0;

        /// <summary>How far each particle is drawn moved toward the camera, in world units.</summary>
        protected float CameraBias { get; }

        /// <summary>Control point carrying a hue shift, saturation and lightness scale. Negative for none.</summary>
        protected int HsvShiftControlPoint { get; } = -1;

        /// <summary>The order the particles are drawn in.</summary>
        private ParticleSortingChoiceList SortMethod { get; }

        /// <summary>Whether this renderer draws only into the water effects map, never into the scene.</summary>
        public bool OnlyRenderInEffectsWaterPass { get; }

        /// <summary>Render in the bloom effects.</summary>
        public bool OnlyRenderInEffectsBloomPass { get; }

        /// <summary>Whether what this renderer draws is an image; the water effects map takes data instead.</summary>
        protected bool OutputIsColor => !OnlyRenderInEffectsWaterPass;

        /// <summary>Whether this renderer samples the opaque scene depth.</summary>
        public virtual bool WantsSceneDepth => false;

        /// <summary>Whether this renderer samples the opaque scene colour.</summary>
        public virtual bool WantsSceneColor => false;

        private readonly Scene? scene;

        /// <summary>Whether this renderer is lit by the scene. The water effects map takes data rather than an image, so it never is.</summary>
        private bool LitByScene => scene != null && OutputIsColor;

        /// <summary>Whether this renderer is lit from the scene's light probes.</summary>
        protected bool LitByProbes => LitByScene && scene!.LightingInfo.HasValidLightProbes;

        protected ParticleFunctionRenderer(ParticleDefinitionParser parse, Scene? scene = null) : base(parse)
        {
            this.scene = scene;

            OnlyRenderInEffectsWaterPass = parse.Boolean("m_bOnlyRenderInEffectsWaterPass", false);
            OnlyRenderInEffectsBloomPass = parse.Boolean("m_bOnlyRenderInEffectsBloomPass", false);
            RadiusScale = parse.NumberProvider("m_flRadiusScale", RadiusScale);
            AlphaScale = parse.NumberProvider("m_flAlphaScale", AlphaScale);
            RollScale = parse.NumberProvider("m_flRollScale", RollScale);
            ColorScale = parse.VectorProvider("m_vecColorScale", ColorScale);
            ColorBlendType = parse.Enum("m_nColorBlendType", ColorBlendType);
            Alpha2Field = parse.ParticleField("m_nAlpha2Field", Alpha2Field);

            if (parse.Data.GetSubCollection("VisibilityInputs") is { } visibilityInputs)
            {
                CameraBias = parse.Nested(visibilityInputs).Float("m_flCameraBias", CameraBias);
            }

            SortMethod = parse.Enum("m_nSortMethod", SortMethod);
            HsvShiftControlPoint = parse.Int32("m_nHSVShiftControlPoint", HsvShiftControlPoint);
        }

        /// <summary>Creates the static combos for this renderer's shader, lit from the scene when it draws an image.</summary>
        protected Dictionary<string, byte> CreateShaderArguments()
            => LitByScene ? scene!.LightingInfo.CreateShaderArguments() : [];

        /// <summary>
        /// The hue shift, saturation and lightness scale carried by <see cref="HsvShiftControlPoint"/>,
        /// or the identity when no control point is bound. The control point holds the two scales
        /// less one, so a zeroed one is the identity too.
        /// </summary>
        protected Vector3 GetHsvShift(ParticleSystemState systemState)
            => HsvShiftControlPoint >= 0
                ? systemState.GetControlPoint(HsvShiftControlPoint).Position + new Vector3(0f, 1f, 1f)
                : new Vector3(0f, 1f, 1f);

        /// <summary>The renderer scales, evaluated once for a draw of the whole collection.</summary>
        /// <param name="Radius">Scales every particle's radius.</param>
        /// <param name="Alpha">Scales every particle's alpha.</param>
        /// <param name="Roll">Scales every particle's roll.</param>
        /// <param name="Color">The colour scale converted for <see cref="ParticleColorBlend.Apply"/>, with the alpha scale in w.</param>
        protected readonly record struct RendererScales(float Radius, float Alpha, float Roll, Vector4 Color);

        /// <summary>Evaluates the renderer scales for one draw.</summary>
        protected RendererScales EvaluateScales(ParticleSystemState systemState)
        {
            var alpha = AlphaScale.NextNumber(systemState);

            return new RendererScales(
                RadiusScale.NextNumber(systemState),
                alpha,
                RollScale.NextNumber(systemState),
                new Vector4(ParticleColorBlend.ConvertScale(ColorBlendType, ColorScale.NextVector(systemState)), alpha));
        }

        /// <summary>
        /// The particles as they stand at the time the system was asked to reach. That is
        /// <see cref="ParticleCollection.Current"/>, unless the last step carried the system past that
        /// time: then each particle is blended back from its state before the step, and the ones created
        /// during the step get no alpha.
        /// </summary>
        protected static ParticleSpan GetBlendedParticles(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (particles.Count == 0 || systemState.Data?.TryGetRenderBlend(out var fraction) != true)
            {
                return particles.Current;
            }

            return particles.Blended(fraction);
        }

        /// <summary>
        /// How much of the view a sphere fills, from 0 to 1: its radius over its distance, scaled by the
        /// projection, or all of it when the camera is inside the sphere.
        /// </summary>
        protected static float ScreenSizeFraction(Vector3 center, float radius, Camera camera)
        {
            var distanceSquared = Vector3.DistanceSquared(center, camera.Location);

            if (radius * radius > distanceSquared)
            {
                return 1f;
            }

            return MathUtils.Saturate(radius * camera.ProjectionMatrix.M22 / MathF.Sqrt(distanceSquared));
        }

        /// <summary>A particle's position moved toward the camera by <see cref="CameraBias"/>.</summary>
        protected Vector3 BiasTowardCamera(Vector3 position, Vector3 cameraPosition)
            => CameraBias == 0f
                ? position
                : position + (MathUtils.SafeNormalize(cameraPosition - position) * CameraBias);

        /// <summary>The <see cref="Alpha2Field"/> of every particle in <paramref name="particles"/>, or 1 for each when none is set.</summary>
        protected ReadOnlySpan<float> ReadAlpha2(ParticleSpan particles)
        {
            if (Alpha2Field is ParticleField.NoneDisabled or < 0)
            {
                var ones = ParticleScratch.Floats(particles.Length);
                ones.Fill(1f);
                return ones;
            }

            return particles.ReadFloats(Alpha2Field);
        }

        /// <summary>
        /// A particle's colour and alpha combined with the colour and alpha scale, in 0 to 1. The alpha
        /// is taken with its <see cref="Alpha2Field"/> value <paramref name="alpha2"/> and
        /// <paramref name="alphaPrescale"/>, and both are clamped to bytes before they combine.
        /// </summary>
        protected Vector4 BlendColor(Vector3 color, float alpha, float alpha2, in RendererScales scales, float alphaPrescale = 1f)
            => ParticleColorBlend.Apply(ColorBlendType,
                ParticleColorBlend.ToBytes(color, alphaPrescale * alpha * alpha2),
                scales.Color) / 255f;

        /// <summary>
        /// Whether the particles are drawn farthest from the camera first rather than newest first.
        /// </summary>
        protected bool SortsByDistance => SortMethod == ParticleSortingChoiceList.PARTICLE_SORTING_NEAREST;

        /// <summary>
        /// Fills <paramref name="order"/> with one entry per particle, farthest from the camera first.
        /// The low half of an entry is the particle's index.
        /// </summary>
        protected static void SortBackToFront(ParticleSpan particles, Vector3 cameraPosition, Span<ulong> order)
        {
            var positions = particles.ReadVectors(ParticleField.Position);

            for (var i = 0; i < order.Length; i++)
            {
                var distance = Vector3.DistanceSquared(positions[i], cameraPosition);

                // A distance is never negative, so its bits order as an integer, and inverting them
                // puts the farthest first. The index below them keeps coincident particles in a
                // steady order from frame to frame.
                order[i] = ((ulong)~BitConverter.SingleToUInt32Bits(distance) << 32) | (uint)i;
            }

            order.Sort();
        }

        /// <summary>The pass this renderer draws in.</summary>
        public RenderPass Pass { get; protected set; } = RenderPass.Translucent;

        /// <summary>
        /// The scene node the system this belongs to renders under, when it was created for one.
        /// Set by the system renderer; renderers that light themselves read its bindings through it.
        /// </summary>
        public SceneNode? OwnerNode { get; set; }

        /// <summary>Called as the system finishes each step of a frame. Runs on the pool, so touch only what this renderer owns.</summary>
        /// <param name="particles">The system's particles, as the step left them.</param>
        /// <param name="systemState">The state the system's functions read it through.</param>
        public virtual void Simulate(ParticleCollection particles, ParticleSystemState systemState)
        {
        }

        /// <summary>
        /// Called once the initializers have run over particles the system just created. Runs on the
        /// pool, so touch only what this renderer owns.
        /// </summary>
        /// <param name="particles">The system's particles.</param>
        /// <param name="systemState">The state the system's functions read it through.</param>
        /// <param name="first">The index of the first new particle.</param>
        /// <param name="count">How many particles were created.</param>
        public virtual void InitializeParticles(ParticleCollection particles, ParticleSystemState systemState, int first, int count)
        {
        }

        /// <summary>The particle events this renderer wants through <see cref="OnParticleEvent"/>.</summary>
        public virtual ParticleEventType ObservedEvents => ParticleEventType.PARTICLE_EVENT_TYPE_MASK_NONE;

        /// <summary>
        /// Called when particles of the system raise an event, during the step. Runs on the pool, so
        /// touch only what this renderer owns.
        /// </summary>
        /// <param name="particles">The system's particles; particles being killed are still in place.</param>
        /// <param name="eventType">The event, a single flag.</param>
        /// <param name="indices">The indices of the particles that raised it, ascending.</param>
        public virtual void OnParticleEvent(ParticleCollection particles, ParticleEventType eventType, ReadOnlySpan<int> indices)
        {
        }

        /// <summary>Finishes work done on <see cref="Simulate"/>, for what reaches past the system.</summary>
        /// <param name="particles">The system's particles, as the step left them.</param>
        /// <param name="systemState">The state the system's functions read it through.</param>
        public virtual void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
        }

        /// <summary>
        /// Called in place of <see cref="Act"/> while the system does not draw: hidden, on a disabled
        /// layer, stopped, out of draw distance or faded out. Renderers that place nodes or decals in the
        /// scene take them away here.
        /// </summary>
        public virtual void Hide()
        {
        }

        /// <summary>
        /// Whether this renderer draws in the passes of the node it belongs to. Renderers that draw
        /// through scene nodes or decals of their own do not.
        /// </summary>
        public virtual bool DrawsInPasses => true;

        /// <summary>Uploads this renderer's vertex buffer. Called once a frame, before any pass draws.</summary>
        public virtual void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
        }

        public abstract void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera);

        /// <summary>Whether this renderer draws itself into <see cref="RenderPass.DepthOnly"/>. Set alongside <see cref="Pass"/>.</summary>
        public bool CanRenderDepth { get; protected set; }

        /// <summary>Draws depth only.</summary>
        public virtual void RenderDepth(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
        }

        /// <summary>
        /// Whether <see cref="RenderReplacement"/> draws anything. Only the renderers that hand over
        /// world space vertices can; the rest expand their geometry in their own vertex shader.
        /// </summary>
        public virtual bool CanRenderReplacement => false;

        /// <summary>
        /// Draws with a pass replacement shader, such as the one picking draws with.
        /// </summary>
        /// <param name="replacement">The program the pass replaced the material shaders with.</param>
        /// <param name="objectId">The owning scene node's id, drawn as the instancing base.</param>
        public virtual void RenderReplacement(Shader replacement, uint objectId)
        {
        }

        /// <summary>Draws indexed geometry with the object id as the instancing base, which is how
        /// replacement shaders read it.</summary>
        protected static void DrawReplacement(Shader replacement, uint objectId, int vaoHandle, int indexCount, DrawElementsType indexType)
        {
            if (indexCount == 0)
            {
                return;
            }

            VertexArray.Bind(vaoHandle, replacement);

            GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indexCount, indexType, 0, 1, objectId);
        }

        /// <summary>
        /// The sheet this renderer animates its particles with, or null when the textures it draws
        /// with carry none.
        /// </summary>
        public virtual Texture.SpritesheetData? SpriteSheet => null;

        /// <summary>
        /// Replaces the texture this renderer draws with.
        /// </summary>
        public virtual void SetTextureOverride(RenderTexture texture) { }

        /// <summary>
        /// Replaces the material this renderer draws with, for renderers that draw with one.
        /// </summary>
        public virtual void SetMaterialOverride(RenderMaterial material) { }

        public virtual void SetRenderMode(string renderMode) { }
        public virtual IEnumerable<string> GetSupportedRenderModes() => [];
        public virtual void Delete() { }
    }
}
