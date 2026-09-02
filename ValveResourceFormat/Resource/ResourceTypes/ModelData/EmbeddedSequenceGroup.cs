using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData
{
    /// <summary>
    /// A model's embedded animation data: the animation and decode blocks, plus the legacy (AG1)
    /// sequence group holding the bone masks, morph masks, pose parameters and faceposer folders its
    /// sequences are written against. Empty or null throughout for a model that carries none.
    /// </summary>
    public sealed class EmbeddedSequenceGroup
    {
        /// <summary>
        /// The group a model with no embedded animation at all gets.
        /// </summary>
        public static EmbeddedSequenceGroup Empty { get; } = new();

        /// <summary>
        /// Gets the compiled animation data, or <see langword="null"/> when the model carries none.
        /// </summary>
        public KVObject? AnimationData { get; }

        /// <summary>
        /// Gets the animation group's decode key, or <see langword="null"/> when the model carries no
        /// embedded animation.
        /// </summary>
        public KVObject? DecodeKey { get; }

        /// <summary>
        /// Gets the compiled legacy sequence group, or <see langword="null"/> when there is none.
        /// </summary>
        public KVObject? SequenceData { get; }

        private EmbeddedSequenceGroup()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbeddedSequenceGroup"/> class by resolving a
        /// model's <c>embedded_animation</c> block references.
        /// </summary>
        public EmbeddedSequenceGroup(Resource resource)
        {
            ArgumentNullException.ThrowIfNull(resource);

            var ctrl = resource.GetBlockByType(BlockType.CTRL) as BinaryKV3;
            var embeddedAnimation = ctrl?.Data.Root.GetSubCollection("embedded_animation");

            if (embeddedAnimation == null)
            {
                return;
            }

            var animationGroup = resource.GetBlockByIndex((int)embeddedAnimation.GetIntegerProperty("group_data_block")) as KeyValuesOrNTRO;
            DecodeKey = animationGroup?.Data.GetSubCollection("m_decodeKey");

            var animationDataBlock = resource.GetBlockByIndex((int)embeddedAnimation.GetIntegerProperty("anim_data_block")) as KeyValuesOrNTRO;
            AnimationData = animationDataBlock?.Data;

            // Index zero is the model's own data block.
            var sequenceBlockIndex = embeddedAnimation.GetIntegerProperty("seqgroup_data_block");

            if (sequenceBlockIndex > 0)
            {
                SequenceData = (resource.GetBlockByIndex((int)sequenceBlockIndex) as KeyValuesOrNTRO)?.Data;
            }
        }

        /// <summary>
        /// Gets the faceposer folders, mapping each animation name to the folder it is filed under.
        /// </summary>
        public Dictionary<string, string> GetFaceposerFolders()
        {
            var faceposerFolders = SequenceData?.GetSubCollection("m_keyValues")?.GetSubCollection("faceposer_folders");

            if (faceposerFolders == null)
            {
                return [];
            }

            var animationToFolder = new Dictionary<string, string>();

            foreach (var folder in faceposerFolders)
            {
                var animationNames = faceposerFolders.GetArray<string>(folder.Key);

                foreach (var animationName in animationNames ?? [])
                {
                    animationToFolder[animationName] = folder.Key;
                }
            }

            return animationToFolder;
        }

        /// <summary>
        /// Gets the named bone masks, mapping each mask name to its per-bone weights. A sequence names
        /// the mask it plays with through <see cref="SequenceAnimation.BoneMaskName"/>.
        /// </summary>
        public Dictionary<string, Dictionary<string, float>> GetBoneMasks()
        {
            var boneMaskArray = SequenceData?.GetArray("m_localBoneMaskArray");
            var boneNameArray = SequenceData?.GetArray<string>("m_localBoneNameArray");

            if (boneMaskArray == null || boneNameArray == null)
            {
                return [];
            }

            var masks = new Dictionary<string, Dictionary<string, float>>(boneMaskArray.Count);

            foreach (var boneMask in boneMaskArray)
            {
                var boneIndices = boneMask.GetIntegerArray("m_nLocalBoneArray");

                if (boneIndices.Length == 0)
                {
                    continue;
                }

                var boneWeights = boneMask.GetFloatArray("m_flBoneWeightArray");
                var weights = new Dictionary<string, float>(boneIndices.Length);

                for (var i = 0; i < boneIndices.Length; i++)
                {
                    weights[boneNameArray[(int)boneIndices[i]]] = boneWeights[i];
                }

                masks[boneMask.GetStringProperty("m_sName")] = weights;
            }

            return masks;
        }

        /// <summary>
        /// Gets the named morph controller masks, mapping each mask name to a weight for every flex
        /// controller. A controller the mask does not list carries the mask's own default weight, itself
        /// 1 when the compiled data omits it. A mask name scopes both bones
        /// (<see cref="GetBoneMasks"/>) and flex controllers.
        /// </summary>
        public Dictionary<string, Dictionary<string, float>> GetMorphMasks(FlexController[] flexControllers)
        {
            ArgumentNullException.ThrowIfNull(flexControllers);

            var boneMaskArray = SequenceData?.GetArray("m_localBoneMaskArray");

            if (boneMaskArray == null || flexControllers.Length == 0)
            {
                return [];
            }

            var masks = new Dictionary<string, Dictionary<string, float>>();

            foreach (var boneMask in boneMaskArray)
            {
                var defaultWeight = boneMask.GetFloatProperty("m_flDefaultMorphCtrlWeight", 1f);
                var morphWeightArray = boneMask.GetArray("m_morphCtrlWeightArray");

                if (defaultWeight == 1f && (morphWeightArray == null || morphWeightArray.Count == 0))
                {
                    continue;
                }

                var weights = new Dictionary<string, float>(flexControllers.Length);

                foreach (var controller in flexControllers)
                {
                    weights[controller.Name] = defaultWeight;
                }

                foreach (var morphWeightPair in morphWeightArray ?? [])
                {
                    weights[(string)morphWeightPair[0]] = (float)morphWeightPair[1];
                }

                masks[boneMask.GetStringProperty("m_sName")] = weights;
            }

            return masks;
        }

        /// <summary>
        /// Gets the named pose parameters. A blend sequence's
        /// <see cref="SequenceAnimation.PoseParameterNames"/> names one of these per blend dimension.
        /// </summary>
        public List<PoseParameter> GetPoseParameters()
        {
            var poseParamArray = SequenceData?.GetArray("m_localPoseParamArray");

            if (poseParamArray == null || poseParamArray.Count == 0)
            {
                return [];
            }

            var poseParameters = new List<PoseParameter>(poseParamArray.Count);

            foreach (var poseParam in poseParamArray)
            {
                poseParameters.Add(new PoseParameter(
                    poseParam.GetStringProperty("m_sName"),
                    poseParam.GetFloatProperty("m_flStart"),
                    poseParam.GetFloatProperty("m_flEnd"),
                    poseParam.GetBooleanProperty("m_bLooping")));
            }

            return poseParameters;
        }
    }
}
