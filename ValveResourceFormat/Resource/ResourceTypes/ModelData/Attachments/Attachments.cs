using System.Collections;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelData.Attachments
{
    public class Attachments : IEnumerable<Attachment>
    {
        private readonly Attachment[] attachments;
        public int Length => attachments.Length;

        public Attachment this[int i]
        {
            get
            {
                return attachments[i];
            }
        }

        public static Attachments FromData(KVObject modelData)
        {
            return new Attachments(modelData.GetArray("m_attachments"));
        }

        private Attachments(KVObject[] attachmentsData)
        {
            attachments = new Attachment[attachmentsData.Length];
            for (var i = 0; i < attachmentsData.Length; i++)
            {
                attachments[i] = new Attachment(attachmentsData[i]);
            }
        }

        public IEnumerator<Attachment> GetEnumerator()
        {
            for (var i = 0; i < attachments.Length; i++)
            {
                yield return attachments[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
