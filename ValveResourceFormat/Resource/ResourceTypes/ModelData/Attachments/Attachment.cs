using System.Collections;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData.Attachments
{
    /// <summary>
    /// Represents an attachment point on a model with associated influences.
    /// </summary>
    public class Attachment : IEnumerable<Attachment.Influence>
    {
        /// <summary>
        /// Represents an influence on an attachment.
        /// </summary>
        public readonly struct Influence
        {
            /// <summary>
            /// Gets the name of the influence.
            /// </summary>
            public string Name { get; init; }

            /// <summary>
            /// Gets the offset of the influence.
            /// </summary>
            public Vector3 Offset { get; init; }

            /// <summary>
            /// Gets the rotation of the influence.
            /// </summary>
            public Quaternion Rotation { get; init; }

            /// <summary>
            /// Gets the weight of the influence.
            /// </summary>
            public float Weight { get; init; }
        }

        /// <summary>
        /// Gets the name of the attachment.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets a value indicating whether rotation should be ignored for this attachment.
        /// </summary>
        public bool IgnoreRotation { get; init; }

        private readonly Influence[] influences;

        /// <summary>
        /// Gets the influence at the specified index.
        /// </summary>
        /// <param name="i">The index of the influence.</param>
        /// <returns>The influence at the specified index.</returns>
        public Influence this[int i]
        {
            get
            {
                return influences[i];
            }
        }

        /// <summary>
        /// Gets the number of influences in this attachment.
        /// </summary>
        public int Length => influences.Length;

        /// <summary>
        /// Initializes a new instance of the <see cref="Attachment"/> class from KeyValues data.
        /// </summary>
        /// <param name="attachmentData">The KeyValues data containing attachment information.</param>
        public Attachment(KVObject attachmentData)
        {
            var valueData = attachmentData.GetSubCollection("value") ?? attachmentData;

            Name = valueData.GetStringProperty("m_name");
            IgnoreRotation = valueData.GetProperty<bool>("m_bIgnoreRotation");

            var influenceNames = valueData.GetArray<string>("m_influenceNames");
            var influenceRotations = valueData.GetArray("m_vInfluenceRotations").Select(v => v.ToQuaternion()).ToArray();
            var influenceOffsets = valueData.GetArray("m_vInfluenceOffsets", v => v.ToVector3());
            var influenceWeights = valueData.GetArray<double>("m_influenceWeights");

            var influenceCount = valueData.GetInt32Property("m_nInfluences");

            influences = new Influence[influenceCount];
            for (var i = 0; i < influenceCount; i++)
            {
                influences[i] = new Influence
                {
                    Name = influenceNames[i],
                    Rotation = influenceRotations[i],
                    Offset = influenceOffsets[i],
                    Weight = (float)influenceWeights[i]
                };
            }
        }

        /// <inheritdoc/>
        public IEnumerator<Influence> GetEnumerator()
        {
            for (var i = 0; i < influences.Length; i++)
            {
                yield return influences[i];
            }
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
