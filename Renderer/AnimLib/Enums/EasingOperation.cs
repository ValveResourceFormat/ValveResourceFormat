namespace ValveResourceFormat.Renderer.AnimLib;

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
