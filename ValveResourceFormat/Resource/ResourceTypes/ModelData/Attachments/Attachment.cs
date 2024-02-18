using System.Collections;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData.Attachments
{
    public class Attachment : IEnumerable<Attachment.Influence>
    {
        public struct Influence
        {
            public string Name { get; init; }
            public Vector3 Offset { get; init; }
            public Quaternion Rotation { get; init; }
            public double Weight { get; init; }
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
            var valueData = attachmentData.GetSubCollection("value");

            Name = valueData.GetStringProperty("m_name");
            IgnoreRotation = valueData.GetProperty<bool>("m_bIgnoreRotation");

            var influenceNames = valueData.GetArray<string>("m_influenceNames");
            var influenceRotations = valueData.GetArray("m_vInfluenceRotations", v =>
            {
                var x = (float)v.GetDoubleProperty("0");
                var y = (float)v.GetDoubleProperty("1");
                var z = (float)v.GetDoubleProperty("2");
                var w = (float)v.GetDoubleProperty("3");
                return new Quaternion(x, y, z, w);
            });
            var influenceOffsets = valueData.GetArray("m_vInfluenceOffsets", v => v.ToVector3());
            var influenceWeights = valueData.GetArray<double>("m_influenceWeights");
            var influenceRootTransforms = valueData.GetArray<bool>("m_bInfluenceRootTransform");

            var influenceCount = valueData.GetInt32Property("m_nInfluences");

            influences = new Influence[influenceCount];
            for (var i = 0; i < influenceCount; i++)
            {
                var influenceName = influenceRootTransforms[i] ? null : influenceNames[i];
                influences[i] = new Influence
                {
                    Name = influenceName,
                    Rotation = influenceRotations[i],
                    Offset = influenceOffsets[i],
                    Weight = influenceWeights[i]
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
