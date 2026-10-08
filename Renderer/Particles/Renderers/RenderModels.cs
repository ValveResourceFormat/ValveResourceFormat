using System.Linq;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Renders each particle as a model from the authored list, placed, turned and scaled by the
    /// particle and tinted by its color. The models are ordinary model scene nodes, so they light,
    /// shadow and depth sort like any other model in the scene; this renderer only keeps one placed
    /// on each live particle.
    /// </summary>
    /// <remarks>
    /// Body groups, sequences and per-particle animation, cloth and material variables are not
    /// applied; every model draws in its bind pose with its default body groups.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderModels">C_OP_RenderModels</seealso>
    internal class RenderModels : ParticleFunctionRenderer
    {
        private readonly Scene scene;
        private readonly (Model Model, float Weight)[] models;
        private readonly float totalWeight;

        private readonly bool ignoreNormal;
        private readonly bool orientZ;
        private readonly bool ignoreRadius;
        private readonly bool localScale;
        private readonly bool suppressTint;
        private readonly int modelScaleControlPoint = -1;
        private readonly IVectorProvider localOffset = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider localRotation = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider componentScale = new LiteralVectorProvider(Vector3.One);
        private readonly INumberProvider manualModelSelection = new LiteralNumberProvider(-1f);
        private readonly INumberProvider skin = new LiteralNumberProvider(0f);
        private readonly INumberProvider rollScale = new LiteralNumberProvider(1f);
        private readonly ParticleField alpha2Field = ParticleField.AlphaAlternate;

        // The node each live particle draws through, keyed by its unique id so it keeps its model
        private readonly Dictionary<int, (ModelSceneNode Node, int Model)> placed = [];
        private readonly Stack<ModelSceneNode>[] idle;
        private readonly HashSet<int> seen = [];
        private readonly List<int> gone = [];

        public RenderModels(ParticleDefinitionParser parse, RendererContext rendererContext, Scene scene) : base(parse, scene)
        {
            this.scene = scene;

            var list = new List<(Model, float)>();

            foreach (var entry in parse.Data.GetArray("m_ModelList") ?? [])
            {
                var path = entry.GetStringProperty("m_model");

                if (string.IsNullOrEmpty(path)
                    || rendererContext.FileLoader.LoadFileCompiled(path)?.DataBlock is not Model model)
                {
                    continue;
                }

                var weight = entry.ContainsKey("m_flRelativeProbabilityOfSpawn") ? entry.GetFloatProperty("m_flRelativeProbabilityOfSpawn") : 1f;
                list.Add((model, MathF.Max(0f, weight)));
            }

            models = [.. list];
            totalWeight = models.Sum(static entry => entry.Weight);
            idle = [.. models.Select(static _ => new Stack<ModelSceneNode>())];

            ignoreNormal = parse.Boolean("m_bIgnoreNormal", ignoreNormal);
            orientZ = parse.Boolean("m_bOrientZ", orientZ);
            ignoreRadius = parse.Boolean("m_bIgnoreRadius", ignoreRadius);
            localScale = parse.Boolean("m_bLocalScale", localScale);
            suppressTint = parse.Boolean("m_bSuppressTint", suppressTint);
            modelScaleControlPoint = parse.Int32("m_nModelScaleCP", modelScaleControlPoint);
            localOffset = parse.VectorProvider("m_vecLocalOffset", localOffset);
            localRotation = parse.VectorProvider("m_vecLocalRotation", localRotation);
            componentScale = parse.VectorProvider("m_vecComponentScale", componentScale);
            manualModelSelection = parse.NumberProvider("m_flManualModelSelection", manualModelSelection);
            skin = parse.NumberProvider("m_nSkin", skin);
            rollScale = parse.NumberProvider("m_flRollScale", rollScale);
            alpha2Field = parse.ParticleField("m_nAlpha2Field", alpha2Field);

            Pass = RenderPass.Opaque;
        }

        /// <inheritdoc/>
        public override void Act(ParticleCollection particles, ParticleSystemState systemState)
        {
            if (models.Length == 0)
            {
                return;
            }

            var shown = OwnerNode is not { Visible: false } && OwnerNode?.LayerEnabled != false;
            var colorScale = ColorScale.NextVector(systemState);
            var alphaScale = AlphaScale.NextNumber(systemState);
            var radiusScale = RadiusScale.NextNumber(systemState);
            var roll = rollScale.NextNumber(systemState);
            var controlPointScale = modelScaleControlPoint >= 0 ? systemState.GetControlPoint(modelScaleControlPoint).Position.X : 1f;

            seen.Clear();

            foreach (ref var particle in particles.Current)
            {
                if (!shown)
                {
                    break;
                }

                seen.Add(particle.UniqueParticleId);

                if (!placed.TryGetValue(particle.UniqueParticleId, out var entry))
                {
                    var model = PickModel(ref particle, systemState);
                    entry = (TakeNode(model, ref particle, systemState), model);
                    placed.Add(particle.UniqueParticleId, entry);
                }

                var node = entry.Node;
                var scale = componentScale.NextVector(ref particle, systemState) * controlPointScale
                    * (ignoreRadius ? 1f : particle.Radius * radiusScale);

                node.Transform = ParticleTransform(ref particle, systemState, scale, roll);
                node.Tint = suppressTint ? Vector3.One : particle.Color * colorScale;
                node.Alpha = particle.Alpha * particle.GetScalar(alpha2Field) * alphaScale;
                node.Visible = true;
            }

            // Particles that died hand their node back, hidden, for the next one of that model
            gone.Clear();

            foreach (var (id, (node, model)) in placed)
            {
                if (!seen.Contains(id))
                {
                    node.Visible = false;
                    idle[model].Push(node);
                    gone.Add(id);
                }
            }

            foreach (var id in gone)
            {
                placed.Remove(id);
            }
        }

        private int PickModel(ref Particle particle, ParticleSystemState systemState)
        {
            var manual = (int)manualModelSelection.NextNumber(ref particle, systemState);

            if (manual >= 0)
            {
                return Math.Min(manual, models.Length - 1);
            }

            if (models.Length == 1 || totalWeight <= 0f)
            {
                return 0;
            }

            var pick = systemState.Random.ForParticle(particle.ParticleId) * totalWeight;

            for (var i = 0; i < models.Length; i++)
            {
                pick -= models[i].Weight;

                if (pick < 0f)
                {
                    return i;
                }
            }

            return models.Length - 1;
        }

        private ModelSceneNode TakeNode(int model, ref Particle particle, ParticleSystemState systemState)
        {
            if (idle[model].TryPop(out var node))
            {
                return node;
            }

            var groups = models[model].Model.GetMaterialGroups().ToArray();
            var skinIndex = (int)skin.NextNumber(ref particle, systemState);
            var skinName = (uint)skinIndex < (uint)groups.Length ? groups[skinIndex].Name : null;

            node = new ModelSceneNode(scene, models[model].Model, skinName);
            scene.Add(node, true);

            return node;
        }

        /// <summary>
        /// Model space to world: scale, the authored local rotation and offset, the particle's own turn,
        /// then its position. Scales apply along the model's axes when authored local, otherwise along
        /// the world's once the model has turned.
        /// </summary>
        private Matrix4x4 ParticleTransform(ref Particle particle, ParticleSystemState systemState, Vector3 scale, float roll)
        {
            var local = localRotation.NextVector(ref particle, systemState);
            var localTurn = EntityTransformHelper.EulerAnglesToRotationMatrix(local)
                * Matrix4x4.CreateTranslation(localOffset.NextVector(ref particle, systemState));

            Matrix4x4 turn;
            var normal = particle.GetVector(ParticleField.Normal);

            if (orientZ && !ignoreNormal && normal.LengthSquared() > 1e-8f)
            {
                // The model's up axis follows the normal
                var up = Vector3.Normalize(normal);
                var forward = MathF.Abs(up.Z) < 0.99f ? Vector3.UnitZ : Vector3.UnitX;
                var right = Vector3.Normalize(Vector3.Cross(up, forward));
                forward = Vector3.Cross(right, up);
                turn = new Matrix4x4(
                    forward.X, forward.Y, forward.Z, 0f,
                    right.X, right.Y, right.Z, 0f,
                    up.X, up.Y, up.Z, 0f,
                    0f, 0f, 0f, 1f);
            }
            else
            {
                // The particle holds yaw, pitch and roll in radians
                var angles = particle.Rotation;
                turn = EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(
                    float.RadiansToDegrees(angles.Y), float.RadiansToDegrees(angles.X), float.RadiansToDegrees(angles.Z) * roll));
            }

            var scaling = Matrix4x4.CreateScale(scale);

            return localScale
                ? scaling * localTurn * turn * Matrix4x4.CreateTranslation(particle.Position)
                : localTurn * turn * scaling * Matrix4x4.CreateTranslation(particle.Position);
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            // The model nodes draw themselves with the rest of the scene
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            foreach (var (node, _) in placed.Values)
            {
                scene.Remove(node, true);
                node.Delete();
            }

            foreach (var stack in idle)
            {
                foreach (var node in stack)
                {
                    scene.Remove(node, true);
                    node.Delete();
                }
            }

            placed.Clear();
        }
    }
}
