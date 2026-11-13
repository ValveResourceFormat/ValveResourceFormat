namespace ValveResourceFormat;

/// <summary>
/// How a model attached by a model config is placed on the parent model.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/ModelConfigAttachmentType_t">ModelConfigAttachmentType_t</seealso>
public enum ModelConfigAttachmentType
{
    /// <summary>Not placed.</summary>
    Invalid = -1,

    /// <summary>At an attachment point or bone of the parent, moved by the offset.</summary>
    BoneOrAttachment = 0,

    /// <summary>At the root of the parent, moved by the offset.</summary>
    RootRelative = 1,

    /// <summary>Posed by the bones of the parent that have the same bone names.</summary>
    Bonemerge = 2,
}
