using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that visualizes model hitbox sets for debugging.
    /// </summary>
    public class HitboxSetSceneNode : SceneNode
    {
        class HitboxSetData
        {
            public required Hitbox[] HitboxSet { get; init; }
            public required HitboxSceneNode[] SceneNodes { get; init; }
            public required int[] HitboxBoneIndexes { get; init; }
        }

        readonly AnimationController animationController;

        readonly Dictionary<string, HitboxSetData> hitboxSets = [];
        HitboxSetData? currentSet;
        Skeleton skeleton => animationController.FrameCache.Skeleton;

        /// <summary>
        /// Initializes a new instance of the <see cref="HitboxSetSceneNode"/> class and builds scene nodes for all hitbox sets.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="animationController">The animation controller providing bone pose data.</param>
        /// <param name="hitboxSets">Named sets of hitboxes to visualize.</param>
        public HitboxSetSceneNode(Scene scene, AnimationController animationController, Dictionary<string, Hitbox[]> hitboxSets)
            : base(scene)
        {
            this.animationController = animationController;

            var boneIndexes = skeleton.Bones.Select((b, i) => (b, i))
                                            .ToDictionary(p => p.b.Name.ToLowerInvariant(), p => p.i);

            foreach (var pair in hitboxSets)
            {
                AddHitboxSet(pair.Key, pair.Value, boneIndexes);
            }
        }

        private void AddHitboxSet(string name, Hitbox[] hitboxSet, Dictionary<string, int> boneIndexes)
        {
            var sceneNodes = new HitboxSceneNode[hitboxSet.Length];
            var hitboxBoneIndexes = new int[hitboxSet.Length];

            for (var i = 0; i < hitboxSet.Length; i++)
            {
                var hitbox = hitboxSet[i];
                var sceneNode = HitboxSceneNode.Create(Scene, hitbox);

                // Registered with the scene so it gets an instance slot for its transform, updated and shown by this node
                sceneNode.Parent = this;
                sceneNode.Visible = false;
                Scene.Add(sceneNode, true);

                sceneNodes[i] = sceneNode;

                if (string.IsNullOrEmpty(hitbox.BoneName) || !boneIndexes.TryGetValue(hitbox.BoneName.ToLowerInvariant(), out var boneIndex))
                {
                    hitboxBoneIndexes[i] = -1;
                }
                else
                {
                    hitboxBoneIndexes[i] = boneIndex;
                }
            }

            var data = new HitboxSetData
            {
                HitboxSet = hitboxSet,
                HitboxBoneIndexes = hitboxBoneIndexes,
                SceneNodes = sceneNodes
            };

            hitboxSets.Add(name, data);
        }

        /// <summary>
        /// Activates the named hitbox set for rendering, or clears the active set when <paramref name="set"/> is <see langword="null"/>.
        /// </summary>
        public void SetHitboxSet(string? set)
        {
            SetVisible(currentSet, false);
            currentSet = set == null ? null : hitboxSets[set];
            SetVisible(currentSet, true);
        }

        private static void SetVisible(HitboxSetData? hitboxSetData, bool visible)
        {
            if (hitboxSetData == null)
            {
                return;
            }

            foreach (var node in hitboxSetData.SceneNodes)
            {
                node.Visible = visible;
            }
        }

        private static void UpdateHitboxSet(HitboxSetData hitboxSetData, ReadOnlySpan<Matrix4x4> boneMatrices, in Matrix4x4 worldTransform)
        {
            var hitboxSet = hitboxSetData.HitboxSet;
            for (var i = 0; i < hitboxSet.Length; i++)
            {
                var shape = hitboxSetData.SceneNodes[i];
                var hitbox = hitboxSet[i];
                var boneId = hitboxSetData.HitboxBoneIndexes[i];
                var targetTransform = boneId == -1 ? Matrix4x4.Identity : boneMatrices[boneId];

                if (hitbox.TranslationOnly)
                {
                    targetTransform = Matrix4x4.CreateTranslation(targetTransform.Translation);
                }

                shape.Transform = targetTransform * worldTransform;
            }
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (currentSet == null)
            {
                return;
            }

            UpdateHitboxSet(currentSet, animationController.Pose, Transform);
        }
    }
}
