namespace ValveResourceFormat.Renderer.AnimLib;

enum CachedValueMode : uint
{
    OnEntry = 0,
    OnExit = 1,
}

enum EasingFunction : byte
{
    Linear = 0,
    Quad = 1,
    Cubic = 2,
    Quart = 3,
    Quint = 4,
    Sine = 5,
    Expo = 6,
    Circ = 7,
    Back = 8,
}

/// <summary>An easing curve applied to a 0 to 1 blend.</summary>
public enum EasingOperation : byte
{
    /// <summary>No easing.</summary>
    Linear = 0,
    /// <summary>Ease in with a quadratic curve.</summary>
    InQuad = 1,
    /// <summary>Ease out with a quadratic curve.</summary>
    OutQuad = 2,
    /// <summary>Ease in and out with a quadratic curve.</summary>
    InOutQuad = 3,
    /// <summary>Ease in with a cubic curve.</summary>
    InCubic = 4,
    /// <summary>Ease out with a cubic curve.</summary>
    OutCubic = 5,
    /// <summary>Ease in and out with a cubic curve.</summary>
    InOutCubic = 6,
    /// <summary>Ease in with a quartic curve.</summary>
    InQuart = 7,
    /// <summary>Ease out with a quartic curve.</summary>
    OutQuart = 8,
    /// <summary>Ease in and out with a quartic curve.</summary>
    InOutQuart = 9,
    /// <summary>Ease in with a quintic curve.</summary>
    InQuint = 10,
    /// <summary>Ease out with a quintic curve.</summary>
    OutQuint = 11,
    /// <summary>Ease in and out with a quintic curve.</summary>
    InOutQuint = 12,
    /// <summary>Ease in with a sine curve.</summary>
    InSine = 13,
    /// <summary>Ease out with a sine curve.</summary>
    OutSine = 14,
    /// <summary>Ease in and out with a sine curve.</summary>
    InOutSine = 15,
    /// <summary>Ease in with a exponential curve.</summary>
    InExpo = 16,
    /// <summary>Ease out with a exponential curve.</summary>
    OutExpo = 17,
    /// <summary>Ease in and out with a exponential curve.</summary>
    InOutExpo = 18,
    /// <summary>Ease in with a circular curve.</summary>
    InCirc = 19,
    /// <summary>Ease out with a circular curve.</summary>
    OutCirc = 20,
    /// <summary>Ease in and out with a circular curve.</summary>
    InOutCirc = 21,
    /// <summary>No easing applied.</summary>
    None = 22,
}

enum EventConditionRules : byte
{
    LimitSearchToSourceState = 0,
    IgnoreInactiveEvents = 1,
    PreferHighestWeight = 2,
    PreferHighestProgress = 3,
    OperatorOr = 4,
    OperatorAnd = 5,
    SearchOnlyGraphEvents = 6,
    SearchOnlyAnimEvents = 7,
    SearchBothGraphAndAnimEvents = 8,
}

enum EventRelevance : uint
{
    ClientOnly = 0,
    ServerOnly = 1,
    ClientAndServer = 2,
}

enum EventTargetEntity : uint
{
    Self = 0,
    Weapon = 1,
    HeldItem = 2,
    Custom = 3,
}

enum FollowBoneMode : byte
{
    RotationAndTranslation = 0,
    RotationOnly = 1,
    TranslationOnly = 2,
}

enum FootPhase : byte
{
    LeftFootDown = 0,
    RightFootPassing = 1,
    RightFootDown = 2,
    LeftFootPassing = 3,
    None = 4,
}

enum FootPhaseCondition : byte
{
    LeftFootDown = 0,
    LeftFootPassing = 1,
    LeftPhase = 4,
    RightFootDown = 2,
    RightFootPassing = 3,
    RightPhase = 5,
    None = 6,
}

enum FrameSnapEventMode : uint
{
    Floor = 0,
    Round = 1,
}

enum GraphDebugMode : uint
{
    Off = 0,
    On = 1,
}

enum GraphEventTypeCondition : byte
{
    Entry = 0,
    FullyInState = 1,
    Exit = 2,
    Timed = 3,
    Generic = 4,
    Any = 5,
}

enum GraphValueType : byte
{
    Unknown = 0,
    Bool = 1,
    ID = 2,
    Float = 3,
    Vector = 4,
    Target = 5,
    BoneMask = 6,
    Pose = 7,
    Special = 8,
}

/// <summary>How an IK result is blended by its weight.</summary>
public enum IKBlendMode : byte
{
    /// <summary>Blend the effector target before solving.</summary>
    Effector = 0,
    /// <summary>Blend the solved bone transforms into the pose.</summary>
    Pose = 1,
}

enum PoseBlendMode : byte
{
    Overlay = 0,
    Additive = 1,
    ModelSpace = 2,
}

enum RootMotionBlendMode : byte
{
    Blend = 0,
    Additive = 1,
    IgnoreSource = 2,
    IgnoreTarget = 3,
}

enum TargetWarpAlgorithm : byte
{
    Lerp = 0,
    Hermite = 1,
    HermiteFeaturePreserving = 2,
    Bezier = 3,
}

enum TargetWarpRule : byte
{
    WarpXY = 0,
    WarpZ = 1,
    WarpXYZ = 2,
    RotationOnly = 3,
    FixedSection = 4,
}

enum TransitionRule : byte
{
    AllowTransition = 0,
    ConditionallyAllowTransition = 1,
    BlockTransition = 2,
}

enum TransitionRuleCondition : byte
{
    AnyAllowed = 0,
    FullyAllowed = 1,
    ConditionallyAllowed = 2,
    Blocked = 3,
}
