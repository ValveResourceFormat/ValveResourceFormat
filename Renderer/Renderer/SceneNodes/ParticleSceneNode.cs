using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.Renderer.Particles.Utils;
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
        /// Whether to load preview control point state, and loop playback when finished.
        /// </summary>
        public bool Preview { get; set; }

        /// <summary>
        /// Whether the system starts over once it has finished. Set for preview playback; off in a
        /// scene, where an effect that has run out stays gone. Changing it takes effect at the next
        /// <see cref="Restart"/>.
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// Which parts of the effect a playback cycle covers. Changing it takes effect at the next
        /// <see cref="Restart"/>.
        /// </summary>
        public ParticlePlaybackMode PlaybackMode { get; set; }

        private bool endCapPlayed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParticleSceneNode"/> class.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="particleSystem">The particle system resource to simulate and render.</param>
        /// <param name="particleSnapshot">Optional snapshot to provide initial particle data (e.g. from a map entity).</param>
        /// <param name="preview">Whether to load preview control point state, and loop playback when finished.</param>
        /// <param name="playedByEntity">
        /// Whether a map entity plays this effect, in which case its control points come from that entity
        /// rather than from a control point configuration.
        /// </param>
        public ParticleSceneNode(Scene scene, ParticleSystem particleSystem, ParticleSnapshot? particleSnapshot = null, bool preview = false, bool playedByEntity = false)
            : base(scene)
        {
            particleRenderer = new ParticleRenderer(particleSystem, Scene.RendererContext, scene, particleSnapshot)
            {
                OwnerNode = this,
            };
            LocalBoundingBox = particleRenderer.LocalBoundingBox;

            RenderPasses = particleRenderer.Passes;

            if (preview)
            {
                Preview = true;
                Loop = true;
                PreviewModel = CreatePreviewModel(particleSystem);
                if (PreviewModel != null)
                {
                    Scene.Add(PreviewModel, true);
                }
            }
            else if (!playedByEntity)
            {
                ApplyRuntimeControlPointValues(particleSystem);
            }
        }

        // An effect dispatched by code plays under a control point configuration, which carries constants its
        // operators depend on as driver offsets. Control point 0 is skipped, it is the effect's placement.
        private void ApplyRuntimeControlPointValues(ParticleSystem particleSystem)
        {
            var data = particleSystem.GetUpgradedData();
            var configurations = data.GetArray("m_controlPointConfigurations");
            if (configurations == null)
            {
                return;
            }

            // Viewmodel effects carry a first-person configuration; everything else plays under "game".
            var viewModelEffect = data.GetStringProperty("m_nViewModelEffect") == "INHERITABLE_BOOL_TRUE";
            var wantedConfiguration = viewModelEffect ? "fps_view" : "game";

            KVObject? chosen = null;

            foreach (var configuration in configurations)
            {
                var name = configuration.GetStringProperty("m_name");

                if (string.Equals(name, wantedConfiguration, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = configuration;
                    break;
                }

                // Any non-preview configuration beats nothing, but keep looking for the wanted one.
                if (chosen == null && !string.Equals(name, "preview", StringComparison.OrdinalIgnoreCase))
                {
                    chosen = configuration;
                }
            }

            var drivers = chosen?.GetArray("m_drivers");
            if (drivers == null)
            {
                return;
            }

            foreach (var driver in drivers)
            {
                var controlPoint = driver.ContainsKey("m_iControlPoint") ? driver.GetInt32Property("m_iControlPoint") : 0;
                if (controlPoint == 0)
                {
                    continue;
                }

                var offset = ReadDriverVector(driver, "m_vecOffset");
                if (offset == Vector3.Zero)
                {
                    continue;
                }

                GetControlPoint(controlPoint).Position = offset;
            }
        }

        /// <summary>
        /// Creates particle nodes for the particle systems referenced by a model's keyvalues
        /// (<c>particles_list</c>) and wires each one to the given model node according to the entry's
        /// <c>attachment_type</c>. Follow types track the model or attachment point while it animates;
        /// the other types are placed once at spawn. Entries with an unknown type are skipped.
        /// </summary>
        /// <param name="scene">The scene the nodes belong to.</param>
        /// <param name="model">The model referencing the particle systems.</param>
        /// <param name="modelNode">The model node providing the attachment points.</param>
        /// <returns>The created particle nodes. The caller adds them to the scene.</returns>
        public static List<ParticleSceneNode> CreateModelParticles(Scene scene, Model model, ModelSceneNode modelNode)
        {
            var particlesList = model.KeyValues.GetArray("particles_list");
            if (particlesList == null)
            {
                return [];
            }

            var nodes = new List<ParticleSceneNode>(particlesList.Count);

            foreach (var entry in particlesList)
            {
                var particleName = entry.GetStringProperty("name");
                if (string.IsNullOrEmpty(particleName))
                {
                    continue;
                }

                try
                {
                    if (scene.RendererContext.FileLoader.LoadFileCompiled(particleName)?.DataBlock is not ParticleSystem particleSystem)
                    {
                        continue;
                    }

                    var attachmentPoint = entry.GetStringProperty("attachment_point") ?? string.Empty;
                    var attachmentTypeName = entry.GetStringProperty("attachment_type");
                    // The keyvalue name is the enum member without the PATTACH_ prefix (e.g. point_follow).
                    var attachmentType = Enum.TryParse<ParticleAttachment>("PATTACH_" + attachmentTypeName, ignoreCase: true, out var parsedType)
                        ? parsedType
                        : ParticleAttachment.PATTACH_INVALID;
                    var offset = ReadDriverVector(entry, "attachment_offset");

                    if (attachmentType == ParticleAttachment.PATTACH_INVALID)
                    {
                        scene.RendererContext.Logger.LogWarning("Unknown attachment type '{Type}' for model particle '{Particle}'", attachmentTypeName, particleName);
                        continue;
                    }

                    var particleNode = new ParticleSceneNode(scene, particleSystem)
                    {
                        Name = particleName,
                    };

                    AttachOnModel(modelNode, particleNode, attachmentType, attachmentPoint, offset);
                    nodes.Add(particleNode);
                }
                catch (Exception e)
                {
                    scene.RendererContext.Logger.LogError(e, "Failed to setup model particle '{Particle}'", particleName);
                }
            }

            return nodes;
        }

        // Maps a ParticleAttachment_t kind onto the model's generic attach primitives: custom origin and
        // world origin are placed once, every other kind tracks the model or its named attachment point.
        private static void AttachOnModel(ModelSceneNode modelNode, SceneNode node, ParticleAttachment attachType, string attachmentName, Vector3 offset)
        {
            switch (attachType)
            {
                case ParticleAttachment.PATTACH_POINT:
                case ParticleAttachment.PATTACH_POINT_FOLLOW:
                    modelNode.AttachNode(node, attachmentName, offset);
                    break;

                case ParticleAttachment.PATTACH_WORLDORIGIN:
                    node.Transform = Matrix4x4.CreateTranslation(offset);
                    break;

                case ParticleAttachment.PATTACH_CUSTOMORIGIN:
                    modelNode.PlaceNode(node, string.Empty, offset);
                    break;

                default:
                    modelNode.AttachNode(node, string.Empty, offset);
                    break;
            }
        }

        /// <summary>
        /// Restarts the particle system at the start of its next update.
        /// </summary>
        public void Restart() => pendingRestart = true;

        private bool pendingRestart;

        /// <summary>
        /// Stops emission and plays the system's endcap, which is what the engine does when something
        /// tells a running effect to end.
        /// </summary>
        public void PlayEndCap()
        {
            particleRenderer.Stop();
            particleRenderer.PlayEndCap();
        }

        /// <summary>
        /// Forces this system's renderers to draw once with temporary particles.
        /// </summary>
        public void Prewarm(Camera camera) => particleRenderer.Prewarm(camera);

        /// <summary>Sets the particle detail tier used by detail-tiered inputs and by child systems.</summary>
        public void SetDetailLevel(ParticleDetailLevel level) => particleRenderer.SetDetailLevel(level);

        /// <summary>
        /// Replaces the texture drawn by every renderer in this system and its children.
        /// </summary>
        public void SetTextureOverride(string textureName) => particleRenderer.SetTextureOverride(textureName);

        /// <inheritdoc cref="SetTextureOverride(string)"/>
        public void SetTextureOverride(RenderTexture texture) => particleRenderer.SetTextureOverride(texture);

        /// <summary>Gets the control point at the given index from the particle renderer.</summary>
        /// <param name="index">The index of the control point to retrieve.</param>
        /// <returns>The control point at the specified index.</returns>
        public ControlPoint GetControlPoint(int index) => particleRenderer.GetControlPoint(index);

        /// <summary>
        /// Places a control point at a transform, taking its position and full orientation frame the
        /// same way control point 0 is seeded from this node's transform.
        /// </summary>
        /// <param name="index">The index of the control point to place.</param>
        /// <param name="transform">The world transform the control point takes.</param>
        public void SetControlPoint(int index, Matrix4x4 transform)
        {
            var controlPoint = particleRenderer.GetControlPoint(index);
            controlPoint.Position = transform.Translation;

            var forward = Vector3.TransformNormal(Vector3.UnitX, transform);

            if (forward.LengthSquared() > Epsilon.LengthSquared)
            {
                controlPoint.Orientation = Vector3.Normalize(forward);
                controlPoint.Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(transform));
            }
        }

        /// <summary>
        /// Places a control point at a transform and keeps it there relative to a scene node, so a
        /// control point bound to an entity follows that entity when something moves it.
        /// </summary>
        /// <param name="index">The index of the control point to place.</param>
        /// <param name="target">The node the control point follows.</param>
        /// <param name="transform">The world transform the control point takes while the node is still.</param>
        public void BindControlPoint(int index, SceneNode target, Matrix4x4 transform)
        {
            controlPointBindings ??= [];
            controlPointBindings.Add(new ControlPointBinding(index, target, target.Transform, transform));
            SetControlPoint(index, transform);
        }

        private List<ControlPointBinding>? controlPointBindings;

        private readonly record struct ControlPointBinding(int Index, SceneNode Target, Matrix4x4 TargetOrigin, Matrix4x4 Transform);

        private void UpdateBoundControlPoints()
        {
            foreach (var binding in controlPointBindings!)
            {
                var moved = binding.Target.Transform;
                var transform = binding.Transform;

                if (moved != binding.TargetOrigin && Matrix4x4.Invert(binding.TargetOrigin, out var toOrigin))
                {
                    transform *= toOrigin * moved;
                }

                SetControlPoint(binding.Index, transform);
            }
        }

        private ModelSceneNode? CreatePreviewModel(ParticleSystem particleSystem)
        {
            var configurations = particleSystem.GetUpgradedData().GetArray("m_controlPointConfigurations");
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
                    point.Orientation = EntityTransformHelper.EulerAnglesToForwardDirection(angleOffset);
                    point.Rotation = EntityTransformHelper.EulerAnglesToQuaternion(angleOffset);
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

        private Matrix4x4? seededTransform;

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (!LayerEnabled)
            {
                return;
            }

            // Control point 0 is seeded from the node transform (position, full rotation frame, and its
            // transformed +X as the forward direction) whenever the transform changes from outside, like a
            // non-follow attachment in game. Between seeds the control point belongs to the simulation:
            // particle functions may move it, and the node transform reflects it back after each step.
            // Preview drives the control point separately.
            if (controlPointBindings != null)
            {
                UpdateBoundControlPoints();
            }

            if (seededTransform != Transform)
            {
                var controlPoint = particleRenderer.MainControlPoint;
                controlPoint.Position = Transform.Translation;

                if (!Preview)
                {
                    var controlPointForward = Vector3.TransformNormal(Vector3.UnitX, Transform);
                    if (controlPointForward.LengthSquared() > Epsilon.LengthSquared)
                    {
                        controlPoint.Orientation = Vector3.Normalize(controlPointForward);
                        controlPoint.Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Transform));
                    }
                }

                seededTransform = Transform;
            }

            if (PreviewModel != null && !string.IsNullOrEmpty(PreviewModelAttachmentPoint))
            {
                particleRenderer.MainControlPoint.Position = PreviewModel.GetAttachmentTransform(PreviewModelAttachmentPoint).Translation;
            }

            var frameTime = context.Timestep * FrametimeMultiplier;

            if (pendingRestart)
            {
                pendingRestart = false;
                endCapPlayed = PlaybackMode == ParticlePlaybackMode.EndCapOnly;

                if (endCapPlayed)
                {
                    particleRenderer.PlayEndCapOnly();
                }
                else
                {
                    particleRenderer.Replay();
                }
            }

            if (frameTime > 0f)
            {
                particleRenderer.SetCameraPosition(context.Camera.Location);
                particleRenderer.Update(frameTime, context.Uptime);

                if (!Preview)
                {
                    ReflectControlPointTransform();
                }

                UpdateBounds();
            }

            // The endcap starts where emission ends, so the particles it decays and freezes are still alive.
            if (PlaybackMode == ParticlePlaybackMode.NormalWithEndCap
                && !endCapPlayed
                && particleRenderer.HasFinishedEmitting(emissionEndIsEnough: true, includeChildren: true))
            {
                PlayEndCap();
                endCapPlayed = true;
            }

            // Restart if all emitters are done and all particles expired
            if (Loop && particleRenderer.IsFinished())
            {
                pendingRestart = true;
            }
        }

        // The node transform mirrors control point 0 after simulation, so movement applied by particle
        // functions carries the node along. The reflected matrix is also recorded as the seeded transform:
        // only an external transform write differs from it and triggers a re-seed.
        private void ReflectControlPointTransform()
        {
            var controlPoint = particleRenderer.MainControlPoint;
            var reflected = controlPoint.Rotation is { } rotation
                ? Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(controlPoint.Position)
                : Matrix4x4.CreateTranslation(controlPoint.Position);

            Transform = reflected;
            seededTransform = reflected;
        }

        // The simulation runs in world space with bounds kept relative to control point 0, so the world
        // box is exact and set directly; the local box is derived so consumers composing
        // LocalBoundingBox with Transform still get a box containing the particles.
        private void UpdateBounds()
        {
            var worldBounds = particleRenderer.LocalBoundingBox.Translate(particleRenderer.MainControlPoint.Position);

            LocalBoundingBox = Matrix4x4.Invert(Transform, out var inverseTransform)
                ? worldBounds.Transform(inverseTransform)
                : particleRenderer.LocalBoundingBox;
            BoundingBox = worldBounds;
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (context.ReplacementShader is not null)
            {
                return;
            }

            if (context.RenderPass is not (RenderPass.Opaque or RenderPass.Translucent or RenderPass.DepthOnly))
            {
                return;
            }

            particleRenderer.Render(context.Camera, context.RenderPass);
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
