using System.Numerics;

namespace GUI.Types.GLViewers;

/// <summary>Display-oriented animation debug snapshot for one demo player.</summary>
public sealed record PlayerAnimDebugData(
    int Slot,
    ulong SteamId,
    string Name,
    string StateLabel,
    string ClipLabel,
    string EventLabel,
    string WarningLabel,
    float Speed,
    bool Grounded,
    float DuckAmount,
    float Pitch,
    float Yaw,
    string Weapon,
    Vector3 WorldPosition,
    bool IsSelected,
    int DetailLevel);
