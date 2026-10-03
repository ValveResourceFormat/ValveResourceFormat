using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class RootMotionOverrideNode : PassthroughNode
{
    [KVProperty("m_desiredMovingVelocityNodeIdx")]
    public short DesiredMovingVelocityNodeIdx { get; } = -1;
    [KVProperty("m_desiredFacingDirectionNodeIdx")]
    public short DesiredFacingDirectionNodeIdx { get; } = -1;
    [KVProperty("m_linearVelocityLimitNodeIdx")]
    public short LinearVelocityLimitNodeIdx { get; } = -1;
    [KVProperty("m_angularVelocityLimitNodeIdx")]
    public short AngularVelocityLimitNodeIdx { get; } = -1;
    [KVProperty("m_enabledNodeIdx")]
    public short EnabledNodeIdx { get; } = -1;
    [KVProperty("m_maxLinearVelocity")]
    public float MaxLinearVelocity { get; } = -1f;
    [KVProperty("m_maxAngularVelocityRadians")]
    public float MaxAngularVelocityRadians { get; } = -1f;
    public BitFlags OverrideFlags { get; }
}
