namespace ValveResourceFormat;

/// <summary>
/// How a model a config attaches is placed on the model it is attached to.
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

    /// <summary>Posed by the bones of the parent that share its bone names.</summary>
    Bonemerge = 2,
}
