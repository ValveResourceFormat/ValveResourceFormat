using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that renders particle system effects.
    /// </summary>
    public class ParticleSceneNode : SceneNode
    {
        private readonly ParticleRenderer particleRenderer;

        /// <summary>
        /// Gets the preview model scene node loaded from particle preview state, if any.
        /// </summary>
        public ModelSceneNode? PreviewModel { get; private set; }

        private string? PreviewModelAttachmentPoint { get; set; }

        /// <summary>Gets or sets a time-scale multiplier applied to the particle simulation each frame.</summary>
        public float FrametimeMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// Whether to load preview control point state. And loop playback when finished.
        /// </summary>
        public bool Preview { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParticleSceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="particleSystem">The particle system resource to simulate and render.</param>
        /// <param name="particleSnapshot">Optional snapshot to provide initial particle data (e.g. from a map entity).</param>
        /// <param name="preview">Whether to load preview control point state. And loop playback when finished.</param>
        public ParticleSceneNode(Scene scene, ParticleSystem particleSystem, ParticleSnapshot? particleSnapshot = null, bool preview = false)
            : base(scene)
        {
            particleRenderer = new ParticleRenderer(particleSystem, Scene.RendererContext, scene, particleSnapshot);
            LocalBoundingBox = particleRenderer.LocalBoundingBox;

            if (preview)
            {
                Preview = true;
                PreviewModel = CreatePreviewModel(particleSystem);
                if (PreviewModel != null)
                {
                    Scene.Add(PreviewModel, true);
                }
            }
        }

        /// <summary>
        /// Restarts the particle system from the beginning.
        /// </summary>
        public void Restart() => particleRenderer.Restart();

        /// <inheritdoc/>
        public ControlPoint GetControlPoint(int index) => particleRenderer.GetControlPoint(index);

        private ModelSceneNode? CreatePreviewModel(ParticleSystem particleSystem)
        {
            var configurations = particleSystem.Data.GetArray("m_controlPointConfigurations");
            if (configurations == null)
            {
                return null;
            }

            KVObject previewConfiguration = null!;
            var previewConfigurationFound = false;

            for (var i = 0; i < configurations.Count; i++)
            {
                var config = configurations[i];
                if (string.Equals(config.GetStringProperty("m_name"), "preview", StringComparison.OrdinalIgnoreCase))
                {
                    previewConfiguration = config;
                    previewConfigurationFound = true;
                    break;
                }
            }

            if (!previewConfigurationFound && configurations.Count > 0)
            {
                previewConfiguration = configurations[0];
                previewConfigurationFound = true;
            }

            if (!previewConfigurationFound)
            {
                return null;
            }

            var previewState = previewConfiguration.GetSubCollection("m_previewState");
            if (previewState == null)
            {
                return null;
            }

            var previewModelPath = previewState.GetStringProperty("m_previewModel");
            if (string.IsNullOrEmpty(previewModelPath))
            {
                return null;
            }

            var previewModelResource = Scene.RendererContext.FileLoader.LoadFileCompiled(previewModelPath);
            if (previewModelResource?.DataBlock is not Model previewModelData)
            {
                return null;
            }

            var previewModelNode = new ModelSceneNode(Scene, previewModelData, null, isWorldPreview: true)
            {
                Name = previewModelData.Name,
            };

            var sequenceName = previewState.GetStringProperty("m_sequenceName");
            if (!string.IsNullOrEmpty(sequenceName))
            {
                previewModelNode.SetAnimationByName(sequenceName);
            }

            var drivers = previewConfiguration.GetArray("m_drivers");
            if (drivers is { Count: > 0 })
            {
                var driver = drivers[0];
                var attachType = driver.GetEnumValue<ParticleAttachment>("m_iAttachType");
                if (attachType is ParticleAttachment.PATTACH_POINT or ParticleAttachment.PATTACH_POINT_FOLLOW)
                {
                    var attachName = driver.GetStringProperty("m_attachmentName");
                    PreviewModelAttachmentPoint = attachName;
                }
            }

            return previewModelNode;
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!LayerEnabled)
            {
                return;
            }

            particleRenderer.MainControlPoint.Position = Transform.Translation;

            if (PreviewModel != null && !string.IsNullOrEmpty(PreviewModelAttachmentPoint))
            {
                particleRenderer.MainControlPoint.Position = PreviewModel.GetAttachmentTransform(PreviewModelAttachmentPoint).Translation;
            }

            var frameTime = context.Timestep * FrametimeMultiplier;

            if (frameTime > 0f)
            {
                particleRenderer.Update(frameTime);
                LocalBoundingBox = particleRenderer.LocalBoundingBox;
            }

            // Restart if all emitters are done and all particles expired
            if (Preview && particleRenderer.IsFinished())
            {
                particleRenderer.Restart();
            }
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.Translucent || context.ReplacementShader is not null)
            {
                return;
            }

            particleRenderer.Render(context.Camera);
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => particleRenderer.GetSupportedRenderModes();

        /// <inheritdoc/>
        public override void SetRenderMode(string mode) => particleRenderer.SetRenderMode(mode);

        /// <inheritdoc/>
        public override void Delete()
        {
            particleRenderer.Delete();
        }
    }
}
