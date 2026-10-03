namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>How an IK result is blended by its weight.</summary>
public enum IKBlendMode : byte
{
    /// <summary>Blend the effector target before solving.</summary>
    Effector = 0,
    /// <summary>Blend the solved bone transforms into the pose.</summary>
    Pose = 1,
}
