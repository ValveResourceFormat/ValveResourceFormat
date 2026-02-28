using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class RootMotionOverrideNode : PassthroughNode
{
    public short DesiredMovingVelocityNodeIdx { get; }
    public short DesiredFacingDirectionNodeIdx { get; }
    public short LinearVelocityLimitNodeIdx { get; }
    public short AngularVelocityLimitNodeIdx { get; }
    public float MaxLinearVelocity { get; }
    public float MaxAngularVelocityRadians { get; }
    public BitFlags OverrideFlags { get; }

    public RootMotionOverrideNode(KVObject data) : base(data)
    {
        DesiredMovingVelocityNodeIdx = data.GetInt16Property("m_desiredMovingVelocityNodeIdx");
        DesiredFacingDirectionNodeIdx = data.GetInt16Property("m_desiredFacingDirectionNodeIdx");
        LinearVelocityLimitNodeIdx = data.GetInt16Property("m_linearVelocityLimitNodeIdx");
        AngularVelocityLimitNodeIdx = data.GetInt16Property("m_angularVelocityLimitNodeIdx");
        MaxLinearVelocity = data.GetFloatProperty("m_maxLinearVelocity");
        MaxAngularVelocityRadians = data.GetFloatProperty("m_maxAngularVelocityRadians");
        OverrideFlags = new(data.GetProperty<KVObject>("m_overrideFlags"));
    }
}
