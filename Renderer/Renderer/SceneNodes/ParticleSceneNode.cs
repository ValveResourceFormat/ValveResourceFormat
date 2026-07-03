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
            particleRenderer = new ParticleRenderer(particleSystem, Scene.RendererContext, scene, particleSnapshot)
            {
                OwnerNode = this,
            };
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

        /// <summary>Sets the particle detail tier (0 = Low .. 3 = Ultra) used by detail-tiered inputs.</summary>
        public void SetDetailLevel(int level) => particleRenderer.SetDetailLevel(level);

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

            var previewModelNode = BuildPreviewModelNode(previewConfiguration);

            // Apply the control-point drivers after the model exists so PATTACH_POINT drivers resolve against
            // its named attachment instead of degrading to the origin.
            ApplyPreviewControlPoints(previewConfiguration, previewModelNode);

            return previewModelNode;
        }

        private ModelSceneNode? BuildPreviewModelNode(KVObject previewConfiguration)
        {
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
                // Some drivers omit m_iAttachType entirely.
                var attachType = driver.ContainsKey("m_iAttachType")
                    ? driver.GetEnumValue<ParticleAttachment>("m_iAttachType")
                    : ParticleAttachment.PATTACH_INVALID;
                if (attachType is ParticleAttachment.PATTACH_POINT or ParticleAttachment.PATTACH_POINT_FOLLOW)
                {
                    var attachName = driver.GetStringProperty("m_attachmentName");
                    PreviewModelAttachmentPoint = attachName;
                }
            }

            return previewModelNode;
        }

        // Applies the preview configuration's control-point drivers so an effect previews with the control
        // points its authoring driver would set (e.g. the path_particle_rope radius/slack on control point 1,
        // which is otherwise (0,0,0) and collapses the rope). Each driver resolves an attach transform, then
        // places the control point at that transform's local-frame offset. With no live entity the driving
        // entity is "self" at the origin: origin-type attaches resolve to identity, point attaches resolve
        // against the preview model's named attachment; anything else degrades to the origin.
        private void ApplyPreviewControlPoints(KVObject previewConfiguration, ModelSceneNode? previewModel)
        {
            var drivers = previewConfiguration.GetArray("m_drivers");
            if (drivers == null)
            {
                return;
            }

            foreach (var driver in drivers)
            {
                var controlPoint = driver.ContainsKey("m_iControlPoint") ? driver.GetInt32Property("m_iControlPoint") : 0;
                // Deadlock drivers commonly omit m_iAttachType entirely.
                var attachType = driver.ContainsKey("m_iAttachType")
                    ? driver.GetEnumValue<ParticleAttachment>("m_iAttachType")
                    : ParticleAttachment.PATTACH_INVALID;
                var offset = ReadDriverVector(driver, "m_vecOffset");
                var angleOffset = ReadDriverVector(driver, "m_angOffset");

                var attachTransform = Matrix4x4.Identity;
                if (previewModel != null && attachType is ParticleAttachment.PATTACH_POINT or ParticleAttachment.PATTACH_POINT_FOLLOW)
                {
                    var attachmentName = driver.GetStringProperty("m_attachmentName");
                    if (!string.IsNullOrEmpty(attachmentName))
                    {
                        attachTransform = previewModel.GetAttachmentTransform(attachmentName);
                    }
                }

                // Offset is applied in the attach point's local frame (rotated by its orientation, then added
                // to its origin); Vector3.Transform composes both for an identity or full attach transform.
                var point = GetControlPoint(controlPoint);
                point.Position = Vector3.Transform(offset, attachTransform);
                if (angleOffset == Vector3.Zero)
                {
                    point.Orientation = Vector3.Zero;
                    point.Rotation = null;
                }
                else
                {
                    point.Orientation = EntityTransformHelper.QAngleToForwardDirection(angleOffset);
                    point.Rotation = Quaternion.CreateFromRotationMatrix(
                        EntityTransformHelper.CreateRotationMatrixFromEulerAngles(angleOffset));
                }
                point.AttachType = attachType;
            }
        }

        // Driver vectors can have null components (e.g. m_angOffset = [null, null, null]),
        // which the plain ToVector3 conversion throws on; GetFloatArray maps null elements to 0.
        private static Vector3 ReadDriverVector(KVObject driver, string key)
        {
            var components = driver.GetFloatArray(key);
            return components is { Length: >= 3 }
                ? new Vector3(components[0], components[1], components[2])
                : Vector3.Zero;
        }

        /// <summary>
        /// Full transform driving control point 0 (position + rotation, e.g. a map entity's origin and
        /// angles). Kept separate from <see cref="SceneNode.Transform"/> because the simulation runs in
        /// world space: the rotation is baked into spawn velocities via the control point, so the node
        /// transform itself must stay translation-only or the bounding box rotates away from the particles.
        /// </summary>
        public Matrix4x4 ControlPointTransform { get; init; } = Matrix4x4.Identity;

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!LayerEnabled)
            {
                return;
            }

            particleRenderer.MainControlPoint.Position = Transform.Translation;

            // The control point also carries the entity's rotation (full frame, plus its transformed +X as
            // the forward direction) so orientation-driven functions read the entity's frame; a degenerate
            // rotation leaves the orientation unset. Preview drives it separately.
            if (!Preview)
            {
                var controlPointForward = Vector3.TransformNormal(Vector3.UnitX, ControlPointTransform);
                if (controlPointForward.LengthSquared() > 1e-12f)
                {
                    particleRenderer.MainControlPoint.Orientation = Vector3.Normalize(controlPointForward);
                    particleRenderer.MainControlPoint.Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(ControlPointTransform));
                }
            }

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

            foreach (var reserved in context.Textures)
            {
                if (reserved.Slot == ReservedTextureSlots.ShadowDepthBufferDepth)
                {
                    particleRenderer.SunShadowDepth = reserved.Texture;
                    break;
                }
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
