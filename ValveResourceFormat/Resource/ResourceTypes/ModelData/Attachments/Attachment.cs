using System.Collections;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData.Attachments
{
    public class Attachment : IEnumerable<Attachment.Influence>
    {
        public readonly struct Influence
        {
            public string Name { get; init; }
            public Vector3 Offset { get; init; }
            public Quaternion Rotation { get; init; }
            public float Weight { get; init; }
        }

        public string Name { get; init; }
        public bool IgnoreRotation { get; init; }

        private readonly Influence[] influences;
        public Influence this[int i]
        {
            get
            {
                return influences[i];
            }
        }
        public int Length => influences.Length;

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

        public IEnumerator<Influence> GetEnumerator()
        {
            for (var i = 0; i < influences.Length; i++)
            {
                yield return influences[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
