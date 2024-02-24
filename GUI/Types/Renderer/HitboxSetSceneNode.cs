using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using OpenTK.Graphics.OpenGL;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    class HitboxSetSceneNode : SceneNode
    {
        class HitboxSetData
        {
            public Hitbox[] HitboxSet { get; init; }
            public HitboxSceneNode[] SceneNodes { get; init; }
            public int[] HitboxBoneIndexes { get; init; }
        }

        readonly AnimationController animationController;
        readonly Skeleton skeleton;

        readonly Matrix4x4[] boneMatrices;
        readonly Dictionary<string, HitboxSetData> hitboxSets = new();
        HitboxSetData currentSet;

        public HitboxSetSceneNode(Scene scene, AnimationController animationController, Skeleton skeleton, Dictionary<string, Hitbox[]> hitboxSets)
            : base(scene)
        {
            this.skeleton = skeleton;
            this.animationController = animationController;
            boneMatrices = new Matrix4x4[skeleton.Bones.Length];

            var boneIndexes = skeleton.Bones.Select((b, i) => (b, i))
                                            .ToDictionary(p => p.b.Name, p => p.i);

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
                sceneNodes[i] = HitboxSceneNode.Create(Scene, hitbox);

                if (string.IsNullOrEmpty(hitbox.BoneName))
                {
                    hitboxBoneIndexes[i] = -1;
                }
                else
                {
                    hitboxBoneIndexes[i] = boneIndexes[hitbox.BoneName];
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

        public void SetHitboxSet(string set)
        {
            if (set == null)
            {
                currentSet = null;
                return;
            }

            currentSet = hitboxSets[set];
        }

        private void UpdateHitboxSet(HitboxSetData hitboxSetData)
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
                    Matrix4x4.Decompose(targetTransform, out _, out _, out var translation);
                    shape.Transform = Matrix4x4.CreateTranslation(translation);
                }
                else
                {
                    shape.Transform = targetTransform;
                }
            }
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (currentSet == null)
            {
                return;
            }

            LocalBoundingBox = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));
            animationController?.GetBoneMatrices(boneMatrices);
            UpdateHitboxSet(currentSet);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (currentSet == null)
            {
                return;
            }

            GL.DepthFunc(DepthFunction.Always);
            foreach (var node in currentSet.SceneNodes)
            {
                node.Render(context);
            }
            GL.DepthFunc(DepthFunction.Greater);
        }
    }
}
