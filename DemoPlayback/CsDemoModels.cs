using System;
using System.Collections.Generic;
using System.Numerics;

namespace ValveResourceFormat.DemoPlayback;

public enum CsDemoTeam
{
    Unknown,
    Spectator,
    Terrorist,
    CounterTerrorist,
}

public enum CsDemoWorldEntityKind
{
    Weapon,
    GrenadeProjectile,
}

public enum CsDemoGrenadeType
{
    None,
    HE,
    Flash,
    Smoke,
    Molotov,
    Incendiary,
    Decoy,
}

public enum CsDemoWorldEffectKind
{
    HEDetonate,
    FlashDetonate,
    MolotovDetonate,
    InfernoFire,
    DecoyDetonate,
}

public enum CsDemoPlayerEffectKind
{
    Blind,
}

public enum CsDemoPlayerModelFallbackReason
{
    ExactCharacterName,
    DefIndexCatalog,
    MapTeamDefault,
    GlobalTeamDefault,
    SafeGlobalFallback,
    BoxProxyFallback,
}

public sealed record CsDemoPlayerInfo(
    int Slot,
    ulong SteamId,
    string Name,
    CsDemoTeam Team);

public sealed record CsDemoRoundSegment(
    int RoundNumber,
    int StartTick,
    int EndTick);

public enum CsDemoTimelineEventKind
{
    Death,
    Kill,
    Bomb,
}

public enum CsDemoPlayerAnimEventKind
{
    Unknown,
    FirePrimary,
    FireSecondary,
    Deploy,
    Reload,
    ThrowGrenade,
    Jump,
    KnifeHit,
    KnifeMiss,
}

public sealed record CsDemoTimelineEvent(
    int Tick,
    CsDemoTimelineEventKind Kind,
    string Label,
    ulong PlayerSteamId,
    string? PlayerName,
    ulong OtherPlayerSteamId,
    string? OtherPlayerName);

public sealed record CsDemoSummary(
    string FileName,
    string MapName,
    string ServerName,
    string ClientName,
    string GameDirectory,
    int TickCount,
    TimeSpan Duration,
    IReadOnlyList<CsDemoPlayerInfo> Players,
    IReadOnlyList<CsDemoRoundSegment> Rounds,
    IReadOnlyList<CsDemoTimelineEvent> TimelineEvents,
    bool Parsed);

public sealed record CsDemoPlayerState(
    int Slot,
    ulong SteamId,
    string Name,
    CsDemoTeam Team,
    bool IsAlive,
    int Health,
    int Armor,
    Vector3 Position,
    float Pitch,
    float Yaw,
    string? ActiveWeapon,
    string? PawnCharacterName,
    ushort PawnCharacterDefIndex,
    Vector3 Velocity = default,
    bool OnGround = true,
    float CrouchBlend = 0f,
    bool IsWalking = false,
    bool IsScoped = false,
    bool IsDefusing = false,
    int ShotsFired = 0,
    Vector2 MovementInput = default);

public sealed record CsDemoPlayerModelResolution(
    string ResolvedModelPath,
    CsDemoPlayerModelFallbackReason FallbackReason,
    string? Detail);

public sealed record CsDemoWorldEntityState(
    uint EntityIndex,
    string ClassName,
    CsDemoWorldEntityKind Kind,
    CsDemoGrenadeType GrenadeType,
    string? ModelIdentity,
    Vector3 Position,
    float Pitch,
    float Yaw,
    float Roll,
    string? OwnerName);

public sealed record CsDemoWorldEffectState(
    string Id,
    CsDemoWorldEffectKind Kind,
    Vector3 Position,
    Vector3 Color,
    int StartTick,
    int EndTick);

public sealed record CsDemoPlayerEffectState(
    CsDemoPlayerEffectKind Kind,
    ulong PlayerSteamId,
    int StartTick,
    int EndTick);

public sealed record CsDemoPlayerAnimEvent(
    int Tick,
    int Slot,
    CsDemoPlayerAnimEventKind Kind,
    int RawEvent,
    int Data);

public sealed record CsDemoFrame(
    int Tick,
    IReadOnlyList<CsDemoPlayerState> Players,
    IReadOnlyList<CsDemoWorldEntityState> WorldEntities,
    IReadOnlyList<CsDemoWorldEffectState> WorldEffects,
    IReadOnlyList<CsDemoPlayerEffectState> PlayerEffects,
    IReadOnlyList<CsDemoPlayerAnimEvent> PlayerAnimEvents)
{
    public static readonly CsDemoFrame Empty = new(0, [], [], [], [], []);
}
