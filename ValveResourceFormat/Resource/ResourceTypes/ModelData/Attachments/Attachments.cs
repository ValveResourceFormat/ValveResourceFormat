using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.ModelData.Attachments.Attachment;

namespace ValveResourceFormat.ResourceTypes.ModelData.Attachments
{
    public class Attachments
    {
        private readonly Attachment[] attachments;
        private readonly Dictionary<string, int> attachmentNameToIndexRemap = new();
        public int Length => attachments.Length;

        public Attachment this[int i]
        {
            get
            {
                return attachments[i];
            }
        }

        public Attachment this[string name]
        {
            get
            {
                var index = attachmentNameToIndexRemap[name];
                return attachments[index];
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
                attachmentNameToIndexRemap.Add(attachments[i].Name, i);
            }
        }
    }
}
