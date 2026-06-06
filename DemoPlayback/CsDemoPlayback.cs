using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DemoFile;
using DemoFile.Game.Cs;

namespace ValveResourceFormat.DemoPlayback;

public sealed class CsDemoPlayback : IDisposable
{
    public static int TickRate => CsDemoParser.TickRate;

    private readonly FileStream stream;
    private readonly CsDemoParser demo;
    private readonly DemoFileReader<CsDemoParser> reader;
    private readonly SemaphoreSlim parserLock = new(1, 1);
    private readonly Lock frameLock = new();
    private readonly List<int> roundStartTicks = [0];
    private readonly List<CsDemoTimelineEvent> timelineEvents = [];
    private readonly List<MutableWorldEffect> worldEffectEvents = [];
    private readonly Dictionary<uint, int> grenadeHiddenFromTick = [];
    private readonly List<CsDemoPlayerEffectState> playerEffectEvents = [];
    private readonly List<CsDemoPlayerAnimEvent> pendingPlayerAnimEvents = [];
    private CsDemoFrame currentFrame = CsDemoFrame.Empty;
    private bool collectingEvents = true;
    private bool disposed;

    private sealed class MutableWorldEffect(string id, CsDemoWorldEffectKind kind, Vector3 position, Vector3 color, int startTick, int endTick)
    {
        public string Id { get; } = id;
        public CsDemoWorldEffectKind Kind { get; } = kind;
        public Vector3 Position { get; } = position;
        public Vector3 Color { get; } = color;
        public int StartTick { get; } = startTick;
        public int EndTick { get; set; } = endTick;

        public CsDemoWorldEffectState ToState() => new(Id, Kind, Position, Color, StartTick, EndTick);
    }

    private CsDemoPlayback(string path, FileStream stream, CsDemoParser demo, DemoFileReader<CsDemoParser> reader)
    {
        FileName = path;
        this.stream = stream;
        this.demo = demo;
        this.reader = reader;
    }

    public string FileName { get; }

    public CsDemoSummary Summary { get; private set; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, TimeSpan.Zero, [], [], [], false);

    public CsDemoFrame CurrentFrame
    {
        get
        {
            using var _ = frameLock.EnterScope();
            return currentFrame;
        }
    }

    public static async Task<CsDemoPlayback> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var demo = new CsDemoParser();
        var reader = DemoFileReader.Create(demo, stream);
        var playback = new CsDemoPlayback(path, stream, demo, reader);
        playback.RegisterTimelineEvents();
        playback.RegisterAnimNetworkEvents();

        try
        {
            await reader.ReadAllAsync(cancellationToken).ConfigureAwait(false);
            playback.collectingEvents = false;
            await reader.SeekToTickAsync(DemoTick.Zero, cancellationToken).ConfigureAwait(false);
            playback.Summary = playback.BuildSummary(parsed: true);
            playback.UpdateSnapshot();

            return playback;
        }
        catch
        {
            playback.Dispose();
            throw;
        }
    }

    public static async Task<CsDemoSummary> ReadSummaryAsync(string path, CancellationToken cancellationToken = default)
    {
        using var playback = await LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return playback.Summary;
    }

    public async Task<CsDemoFrame> SeekToTickAsync(int tick, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await parserLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var targetTick = Math.Clamp(tick, 0, Summary.TickCount);
            await reader.SeekToTickAsync(new DemoTick(targetTick), cancellationToken).ConfigureAwait(false);
            UpdateSnapshot();

            return CurrentFrame;
        }
        finally
        {
            parserLock.Release();
        }
    }

    public async Task<CsDemoFrame> AdvanceToTickAsync(int tick, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await parserLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var targetTick = Math.Clamp(tick, 0, Summary.TickCount);

            if (demo.CurrentDemoTick.Value > targetTick)
            {
                await reader.SeekToTickAsync(new DemoTick(targetTick), cancellationToken).ConfigureAwait(false);
            }

            while (demo.CurrentDemoTick.Value < targetTick)
            {
                if (!await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }

            UpdateSnapshot();

            return CurrentFrame;
        }
        finally
        {
            parserLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        parserLock.Dispose();
        stream.Dispose();
    }

    private void UpdateSnapshot()
    {
        int previousTick;
        using (var readScope = frameLock.EnterScope())
        {
            previousTick = currentFrame.Tick;
        }

        var frame = BuildFrame(previousTick);

        using var writeScope = frameLock.EnterScope();
        currentFrame = frame;
    }

    private CsDemoSummary BuildSummary(bool parsed)
    {
        var header = demo.FileHeader;
        var serverInfo = demo.ServerInfo;
        var mapName = NormalizeMapName(serverInfo?.MapName);

        if (string.IsNullOrEmpty(mapName))
        {
            mapName = NormalizeMapName(header?.MapName);
        }

        mapName = ResolveMapNameAlias(FileName, mapName);

        var tickCount = Math.Max(0, demo.TickCount.Value);
        var tickRate = Math.Max(1, TickRate);
        var duration = TimeSpan.FromSeconds(tickCount / (double)tickRate);
        var players = demo.PlayersIncludingDisconnected
            .Select(ToPlayerInfo)
            .DistinctBy(static player => player.SteamId == 0 ? (ulong)player.Slot : player.SteamId)
            .OrderBy(static player => player.Team)
            .ThenBy(static player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(
            FileName,
            mapName,
            header?.ServerName ?? serverInfo?.HostName ?? string.Empty,
            header?.ClientName ?? string.Empty,
            header?.GameDirectory ?? serverInfo?.GameDir ?? string.Empty,
            tickCount,
            duration,
            players,
            BuildRoundSegments(tickCount),
            timelineEvents
                .OrderBy(static timelineEvent => timelineEvent.Tick)
                .ToArray(),
            parsed);
    }

    private void RegisterTimelineEvents()
    {
        demo.Source1GameEvents.RoundEnd += _ =>
        {
            if (!collectingEvents)
            {
                return;
            }

            var nextRoundTick = Math.Max(0, demo.CurrentDemoTick.Value);

            if (nextRoundTick > 0 && (roundStartTicks.Count == 0 || roundStartTicks[^1] != nextRoundTick))
            {
                roundStartTicks.Add(nextRoundTick);
            }
        };

        demo.Source1GameEvents.PlayerDeath += gameEvent =>
        {
            if (!collectingEvents)
            {
                return;
            }

            var tick = Math.Max(0, demo.CurrentDemoTick.Value);
            var victim = gameEvent.Player?.PlayerName;
            var attacker = gameEvent.Attacker?.PlayerName;
            var weapon = gameEvent.Weapon;

            timelineEvents.Add(new(
                tick,
                CsDemoTimelineEventKind.Death,
                string.IsNullOrWhiteSpace(weapon) ? "death" : weapon,
                gameEvent.Player?.SteamID ?? 0,
                victim,
                gameEvent.Attacker?.SteamID ?? 0,
                attacker));
        };

        demo.Source1GameEvents.BombPlanted += gameEvent =>
        {
            if (!collectingEvents)
            {
                return;
            }

            timelineEvents.Add(new(
                Math.Max(0, demo.CurrentDemoTick.Value),
                CsDemoTimelineEventKind.Bomb,
                "bomb planted",
                gameEvent.Player?.SteamID ?? 0,
                gameEvent.Player?.PlayerName,
                0,
                null));
        };

        demo.Source1GameEvents.HegrenadeDetonate += gameEvent =>
        {
            if (!collectingEvents)
            {
                return;
            }

            RegisterDetonatedGrenadeAtPosition(new Vector3(gameEvent.X, gameEvent.Y, gameEvent.Z), demo.CurrentDemoTick.Value);

            AddTimedWorldEffect(
                $"he:{gameEvent.Entityid}:{demo.CurrentDemoTick.Value}",
                CsDemoWorldEffectKind.HEDetonate,
                new Vector3(gameEvent.X, gameEvent.Y, gameEvent.Z),
                Vector3.One,
                TimeSpan.FromSeconds(2));
        };

        demo.Source1GameEvents.FlashbangDetonate += gameEvent =>
        {
            if (!collectingEvents)
            {
                return;
            }

            RegisterDetonatedGrenadeAtPosition(new Vector3(gameEvent.X, gameEvent.Y, gameEvent.Z), demo.CurrentDemoTick.Value);

            AddTimedWorldEffect(
                $"flash:{gameEvent.Entityid}:{demo.CurrentDemoTick.Value}",
                CsDemoWorldEffectKind.FlashDetonate,
                new Vector3(gameEvent.X, gameEvent.Y, gameEvent.Z),
                Vector3.One,
                TimeSpan.FromSeconds(1.5));
        };

        demo.Source1GameEvents.MolotovDetonate += gameEvent =>
        {
            if (!collectingEvents)
            {
                return;
            }

            RegisterDetonatedGrenadeAtPosition(new Vector3(gameEvent.X, gameEvent.Y, gameEvent.Z), demo.CurrentDemoTick.Value);

            AddTimedWorldEffect(
                $"molotov:{demo.CurrentDemoTick.Value}:{gameEvent.X:F0}:{gameEvent.Y:F0}:{gameEvent.Z:F0}",
                CsDemoWorldEffectKind.MolotovDetonate,
                new Vector3(gameEvent.X, gameEvent.Y, gameEvent.Z),
                new Vector3(1f, 0.42f, 0.08f),
                TimeSpan.FromSeconds(2));
        };

        demo.Source1GameEvents.DecoyDetonate += gameEvent =>
        {
            if (!collectingEvents)
            {
                return;
            }

            RegisterDetonatedGrenadeAtPosition(new Vector3(gameEvent.X, gameEvent.Y, gameEvent.Z), demo.CurrentDemoTick.Value);

            AddTimedWorldEffect(
                $"decoy:{gameEvent.Entityid}:{demo.CurrentDemoTick.Value}",
                CsDemoWorldEffectKind.DecoyDetonate,
                new Vector3(gameEvent.X, gameEvent.Y, gameEvent.Z),
                new Vector3(1f, 0.86f, 0.47f),
                TimeSpan.FromSeconds(0.3));
        };

        demo.Source1GameEvents.PlayerBlind += gameEvent =>
        {
            if (!collectingEvents)
            {
                return;
            }

            var tick = Math.Max(0, demo.CurrentDemoTick.Value);
            var durationTicks = Math.Max(1, (int)Math.Ceiling(gameEvent.BlindDuration * TickRate));
            playerEffectEvents.Add(new(
                CsDemoPlayerEffectKind.Blind,
                gameEvent.Player?.SteamID ?? 0,
                tick,
                tick + durationTicks));
        };
    }

    private void RegisterAnimNetworkEvents()
    {
        demo.GameEvents.FireBullets += msg =>
        {
            var slot = ResolvePlayerSlotFromEntityIndex(msg.Player);
            if (slot < 0)
            {
                return;
            }

            QueuePlayerAnimEvent(slot, CsDemoPlayerAnimEventKind.FirePrimary, rawEvent: 0, data: 0);
        };
    }

    private void QueuePlayerAnimEvent(int slot, CsDemoPlayerAnimEventKind kind, int rawEvent, int data)
    {
        pendingPlayerAnimEvents.Add(new(
            Math.Max(0, demo.CurrentDemoTick.Value),
            slot,
            kind,
            rawEvent,
            data));
    }

    private int ResolvePlayerSlotFromEntityIndex(uint playerHandle)
    {
        var entityIndex = (int)(playerHandle & 0x7FFF);
        if (entityIndex <= 0)
        {
            return -1;
        }

        foreach (var controller in demo.PlayersIncludingDisconnected)
        {
            if (controller.EntityIndex.Value == entityIndex)
            {
                return GetEntitySlot(controller);
            }

            if (controller.PlayerPawn is CCSPlayerPawn pawn && pawn.EntityIndex.Value == entityIndex)
            {
                return GetEntitySlot(controller);
            }
        }

        return -1;
    }

    private CsDemoRoundSegment[] BuildRoundSegments(int tickCount)
    {
        var starts = roundStartTicks
            .Where(tick => tick >= 0 && tick < tickCount)
            .Append(0)
            .Distinct()
            .Order()
            .ToArray();

        if (starts.Length <= 1 && tickCount > 0)
        {
            var fallbackRoundTicks = Math.Max(TickRate * 90, 1);
            starts = Enumerable.Range(0, Math.Max(1, tickCount / fallbackRoundTicks + 1))
                .Select(round => Math.Min(tickCount - 1, round * fallbackRoundTicks))
                .Distinct()
                .ToArray();
        }

        var rounds = new CsDemoRoundSegment[Math.Max(1, starts.Length)];

        for (var i = 0; i < rounds.Length; i++)
        {
            var start = i < starts.Length ? starts[i] : 0;
            var end = i + 1 < starts.Length ? starts[i + 1] : tickCount;
            rounds[i] = new CsDemoRoundSegment(i + 1, start, Math.Max(start, end));
        }

        return rounds;
    }

    private CsDemoFrame BuildFrame(int previousTick)
    {
        var tick = Math.Max(0, demo.CurrentDemoTick.Value);

        var players = demo.PlayersIncludingDisconnected
            .Select(ToPlayerState)
            .Where(static player => player != null)
            .Cast<CsDemoPlayerState>()
            .OrderBy(static player => player.Team)
            .ThenBy(static player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var worldEntityList = demo.Entities
            .Select(entity => ToWorldEntityState(entity, tick))
            .Where(static entity => entity != null)
            .Cast<CsDemoWorldEntityState>()
            .ToList();

        var worldEntities = worldEntityList.Take(256).ToArray();

        var worldEffects = worldEffectEvents
            .Where(effect => IsActiveEffect(effect.StartTick, effect.EndTick, tick))
            .Select(static effect => effect.ToState())
            .Concat(demo.Entities.SelectMany(entity => ToActiveEntityEffects(entity, tick)))
            .Take(256)
            .ToArray();
        var playerEffects = playerEffectEvents
            .Where(effect => IsActiveEffect(effect.StartTick, effect.EndTick, tick))
            .ToArray();
        var playerAnimEvents = pendingPlayerAnimEvents
            .Where(animEvent => IsActiveAnimEvent(animEvent.Tick, previousTick, tick))
            .ToArray();

        return new(tick, players, worldEntities, worldEffects, playerEffects, playerAnimEvents);
    }

    private static bool IsActiveAnimEvent(int eventTick, int previousTick, int tick)
        => previousTick >= 0 && tick > previousTick
            ? eventTick > previousTick && eventTick <= tick
            : eventTick == tick;

    private static CsDemoPlayerInfo ToPlayerInfo(CCSPlayerController player)
    {
        return new(
            GetEntitySlot(player),
            player.SteamID,
            GetPlayerName(player),
            ToTeam(player.CSTeamNum));
    }

    private static CsDemoPlayerState? ToPlayerState(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn;
        var origin = pawn?.Origin ?? player.Origin;
        var angles = pawn?.EyeAngles ?? player.Rotation;
        var isAlive = player.PawnIsAlive && pawn?.IsAlive == true;
        var movementServices = GetPropertyObject(pawn, "MovementServices");
        var velocity = GetPlayerVelocity(pawn);
        var onGround = GetPlayerOnGround(pawn);
        var crouchBlend = GetPlayerCrouchBlend(pawn, movementServices);
        var movementInput = GetPlayerMovementInput(pawn, movementServices);

        return new(
            GetEntitySlot(player),
            player.SteamID,
            GetPlayerName(player),
            ToTeam(player.CSTeamNum),
            isAlive,
            pawn?.Health ?? (int)player.PawnHealth,
            player.PawnArmor,
            ToVector3(origin),
            angles.Pitch,
            angles.Yaw,
            GetActiveWeaponName(player),
            player.PawnCharacterName,
            player.PawnCharacterDefIndex,
            velocity,
            onGround,
            crouchBlend,
            GetBoolProperty(pawn, false, "IsWalking", "Walking"),
            GetBoolProperty(pawn, false, "IsScoped", "Scoped"),
            GetBoolProperty(pawn, false, "IsDefusing", "Defusing"),
            GetIntProperty(pawn, 0, "ShotsFired"),
            movementInput);
    }

    private static Vector3 GetPlayerVelocity(object? pawn)
    {
        foreach (var propertyName in new[] { "AbsVelocity", "Velocity", "BaseVelocity" })
        {
            if (TryGetVector3Property(pawn, propertyName, out var velocity))
            {
                return velocity;
            }
        }

        return Vector3.Zero;
    }

    private static bool GetPlayerOnGround(object? pawn)
    {
        if (TryGetBoolProperty(pawn, "OnGroundLastTick", out var onGround)
            || TryGetBoolProperty(pawn, "OnGround", out onGround))
        {
            return onGround;
        }

        var groundEntity = GetPropertyObject(pawn, "GroundEntity");
        if (groundEntity != null)
        {
            return true;
        }

        var flags = GetIntProperty(pawn, 0, "Flags");
        if (flags != 0)
        {
            const int flOnGround = 1;
            return (flags & flOnGround) != 0;
        }

        return true;
    }

    private static float GetPlayerCrouchBlend(object? pawn, object? movementServices)
    {
        foreach (var propertyName in new[] { "DuckAmount", "Ducked", "Ducking", "DesiresDuck" })
        {
            if (TryGetFloatProperty(movementServices, propertyName, out var value)
                || TryGetFloatProperty(pawn, propertyName, out value))
            {
                return Math.Clamp(value, 0f, 1f);
            }

            if (TryGetBoolProperty(movementServices, propertyName, out var enabled)
                || TryGetBoolProperty(pawn, propertyName, out enabled))
            {
                return enabled ? 1f : 0f;
            }
        }

        return 0f;
    }

    private static Vector2 GetPlayerMovementInput(object? pawn, object? movementServices)
    {
        var forwardMove = GetFloatProperty(movementServices, 0f, "ForwardMove", "Forwardmove");
        var leftMove = GetFloatProperty(movementServices, 0f, "LeftMove", "Leftmove");

        if (forwardMove != 0f || leftMove != 0f)
        {
            return new(leftMove, forwardMove);
        }

        var inputButtons = GetPropertyObject(pawn, "InputButtons");
        if (inputButtons == null)
        {
            return Vector2.Zero;
        }

        var x = 0f;
        var y = 0f;

        if (GetBoolProperty(inputButtons, false, "MoveLeft"))
        {
            x -= 1f;
        }

        if (GetBoolProperty(inputButtons, false, "MoveRight"))
        {
            x += 1f;
        }

        if (GetBoolProperty(inputButtons, false, "Forward"))
        {
            y += 1f;
        }

        if (GetBoolProperty(inputButtons, false, "Back"))
        {
            y -= 1f;
        }

        return new(x, y);
    }

    private CsDemoWorldEntityState? ToWorldEntityState(DemoFile.Sdk.CEntityInstance<CsDemoParser> entity, int tick)
    {
        return entity switch
        {
            CBaseCSGrenadeProjectile grenade when ShouldShowGrenadeProjectile(grenade, tick) => new(
                grenade.EntityIndex.Value,
                CleanEntityClassName(grenade.GetType().Name) ?? "Grenade",
                CsDemoWorldEntityKind.GrenadeProjectile,
                GetGrenadeType(grenade),
                null,
                ToVector3(grenade.Origin),
                grenade.Rotation.Pitch,
                grenade.Rotation.Yaw,
                grenade.Rotation.Roll,
                grenade.Thrower?.Controller?.PlayerName),

            CCSWeaponBase weapon when weapon.OwnerEntity == null && weapon.IsActive && weapon.Origin != default => new(
                weapon.EntityIndex.Value,
                CleanEntityClassName(weapon.GetType().Name) ?? "Weapon",
                CsDemoWorldEntityKind.Weapon,
                CsDemoGrenadeType.None,
                GetWeaponName(weapon),
                ToVector3(weapon.Origin),
                weapon.Rotation.Pitch,
                weapon.Rotation.Yaw,
                weapon.Rotation.Roll,
                null),

            _ => null
        };
    }

    private static IEnumerable<CsDemoWorldEffectState> ToActiveEntityEffects(DemoFile.Sdk.CEntityInstance<CsDemoParser> entity, int tick)
    {
        switch (entity)
        {
            case CInferno inferno when inferno.IsActive:
                {
                    var positions = inferno.FirePositions ?? [];
                    var burning = inferno.FireIsBurning ?? [];
                    var count = Math.Min(inferno.FireCount, Math.Min(positions.Length, burning.Length));
                    var startTick = inferno.FireEffectTickBegin > 0 ? inferno.FireEffectTickBegin : tick;
                    var lifetimeTicks = Math.Max(TickRate, (int)Math.Ceiling(inferno.FireLifetime * TickRate));

                    for (var i = 0; i < count; i++)
                    {
                        if (!burning[i] || positions[i] == default)
                        {
                            continue;
                        }

                        yield return new CsDemoWorldEffectState(
                            $"inferno:{inferno.EntityIndex.Value}:{i}",
                            CsDemoWorldEffectKind.InfernoFire,
                            ToVector3(positions[i]),
                            new Vector3(1f, 0.36f, 0.06f),
                            startTick,
                            startTick + lifetimeTicks);
                    }

                    break;
                }
        }
    }

    private void RegisterDetonatedGrenadeAtPosition(Vector3 position, int tick)
    {
        CBaseCSGrenadeProjectile? closest = null;
        var bestDistSq = 64f * 64f;

        foreach (var entity in demo.Entities)
        {
            if (entity is not CBaseCSGrenadeProjectile grenade || grenade.Origin == default)
            {
                continue;
            }

            var distSq = Vector3.DistanceSquared(ToVector3(grenade.Origin), position);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                closest = grenade;
            }
        }

        if (closest != null)
        {
            grenadeHiddenFromTick[(uint)closest.EntityIndex.Value] = tick;
        }
    }

    private bool ShouldShowGrenadeProjectile(CBaseCSGrenadeProjectile grenade, int tick)
    {
        if (grenade.Origin == default)
        {
            return false;
        }

        var entityId = (uint)grenade.EntityIndex.Value;

        if (!grenade.IsActive)
        {
            return false;
        }

        if (grenadeHiddenFromTick.TryGetValue(entityId, out var hiddenFromTick) && tick >= hiddenFromTick)
        {
            return false;
        }

        return true;
    }

    private void AddTimedWorldEffect(string id, CsDemoWorldEffectKind kind, Vector3 position, Vector3 color, TimeSpan duration)
    {
        var tick = Math.Max(0, demo.CurrentDemoTick.Value);
        worldEffectEvents.Add(new MutableWorldEffect(
            id,
            kind,
            position,
            color,
            tick,
            tick + Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds * TickRate))));
    }

    private static bool IsActiveEffect(int startTick, int endTick, int tick)
    {
        return tick >= startTick && tick <= endTick;
    }

    private static CsDemoGrenadeType GetGrenadeType(CBaseCSGrenadeProjectile grenade)
    {
        var type = grenade switch
        {
            CHEGrenadeProjectile => CsDemoGrenadeType.HE,
            CFlashbangProjectile => CsDemoGrenadeType.Flash,
            CMolotovProjectile molotov => molotov.IsIncGrenade ? CsDemoGrenadeType.Incendiary : CsDemoGrenadeType.Molotov,
            CDecoyProjectile => CsDemoGrenadeType.Decoy,
            _ => CsDemoGrenadeType.None,
        };

        if (type != CsDemoGrenadeType.None)
        {
            return type;
        }

        return GetGrenadeTypeFromClassName(grenade.GetType().Name);
    }

    private static CsDemoGrenadeType GetGrenadeTypeFromClassName(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return CsDemoGrenadeType.None;
        }

        if (typeName.Contains("Flash", StringComparison.OrdinalIgnoreCase))
        {
            return CsDemoGrenadeType.Flash;
        }

        if (typeName.Contains("Decoy", StringComparison.OrdinalIgnoreCase))
        {
            return CsDemoGrenadeType.Decoy;
        }

        if (typeName.Contains("Smoke", StringComparison.OrdinalIgnoreCase))
        {
            return CsDemoGrenadeType.Smoke;
        }

        if (typeName.Contains("Incendiary", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("IncGrenade", StringComparison.OrdinalIgnoreCase))
        {
            return CsDemoGrenadeType.Incendiary;
        }

        if (typeName.Contains("Molotov", StringComparison.OrdinalIgnoreCase))
        {
            return CsDemoGrenadeType.Molotov;
        }

        if (typeName.Contains("HEGrenade", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Hegrenade", StringComparison.OrdinalIgnoreCase))
        {
            return CsDemoGrenadeType.HE;
        }

        return CsDemoGrenadeType.None;
    }

    private static int GetEntitySlot(CBaseEntity entity)
    {
        return checked((int)entity.EntityIndex.Value);
    }

    private static string GetPlayerName(CCSPlayerController player)
    {
        if (!string.IsNullOrWhiteSpace(player.PlayerName))
        {
            return player.PlayerName;
        }

        return player.SteamID == 0 ? $"Player {GetEntitySlot(player)}" : player.SteamID.ToString(CultureInfo.InvariantCulture);
    }

    private static CsDemoTeam ToTeam(CSTeamNumber team)
    {
        return team switch
        {
            CSTeamNumber.Spectator => CsDemoTeam.Spectator,
            CSTeamNumber.Terrorist => CsDemoTeam.Terrorist,
            CSTeamNumber.CounterTerrorist => CsDemoTeam.CounterTerrorist,
            _ => CsDemoTeam.Unknown,
        };
    }

    private static Vector3 ToVector3(DemoFile.Vector vector)
    {
        return new(vector.X, vector.Y, vector.Z);
    }

    private static bool TryGetVector3Property(object? instance, string propertyName, out Vector3 value)
    {
        value = Vector3.Zero;

        if (GetPropertyObject(instance, propertyName) is not { } vector)
        {
            return false;
        }

        if (!TryGetFloatProperty(vector, "X", out var x)
            || !TryGetFloatProperty(vector, "Y", out var y)
            || !TryGetFloatProperty(vector, "Z", out var z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    private static object? GetPropertyObject(object? instance, string propertyName)
    {
        if (instance == null)
        {
            return null;
        }

        var property = instance.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(property => property.Name.Equals(propertyName, StringComparison.Ordinal));

        return property?.GetValue(instance);
    }

    private static bool GetBoolProperty(object? instance, bool fallback, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetBoolProperty(instance, propertyName, out var value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static bool TryGetBoolProperty(object? instance, string propertyName, out bool value)
    {
        value = false;

        if (GetPropertyObject(instance, propertyName) is not { } propertyValue)
        {
            return false;
        }

        if (propertyValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        return false;
    }

    private static int GetIntProperty(object? instance, int fallback, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (GetPropertyObject(instance, propertyName) is { } value)
            {
                return value switch
                {
                    int intValue => intValue,
                    uint uintValue => checked((int)uintValue),
                    short shortValue => shortValue,
                    ushort ushortValue => ushortValue,
                    byte byteValue => byteValue,
                    sbyte sbyteValue => sbyteValue,
                    _ => fallback,
                };
            }
        }

        return fallback;
    }

    private static float GetFloatProperty(object? instance, float fallback, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetFloatProperty(instance, propertyName, out var value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static bool TryGetFloatProperty(object? instance, string propertyName, out float value)
    {
        value = 0f;

        if (GetPropertyObject(instance, propertyName) is not { } propertyValue)
        {
            return false;
        }

        switch (propertyValue)
        {
            case float floatValue:
                value = floatValue;
                return true;
            case double doubleValue:
                value = (float)doubleValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case uint uintValue:
                value = uintValue;
                return true;
            case short shortValue:
                value = shortValue;
                return true;
            case ushort ushortValue:
                value = ushortValue;
                return true;
            case byte byteValue:
                value = byteValue;
                return true;
            case sbyte sbyteValue:
                value = sbyteValue;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeMapName(string? mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return string.Empty;
        }

        var normalized = mapName.Replace('\\', '/').Trim();

        if (normalized.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["maps/".Length..];
        }

        if (normalized.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^".vmap".Length];
        }
        else if (normalized.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^".bsp".Length];
        }

        return normalized;
    }

    private static string ResolveMapNameAlias(string fileName, string mapName)
    {
        if (string.IsNullOrEmpty(mapName) || char.IsDigit(mapName[^1]))
        {
            return mapName;
        }

        var source2Candidate = $"{mapName}2";

        return FileContainsAscii(fileName, source2Candidate) ? source2Candidate : mapName;
    }

    private static bool FileContainsAscii(string fileName, string needleText)
    {
        var needle = Encoding.ASCII.GetBytes(needleText);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024 + needle.Length);

        try
        {
            using var file = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var carry = 0;

            while (true)
            {
                var read = file.Read(buffer, carry, buffer.Length - carry);

                if (read == 0)
                {
                    return false;
                }

                var length = carry + read;

                if (buffer.AsSpan(0, length).IndexOf(needle) >= 0)
                {
                    return true;
                }

                carry = Math.Min(needle.Length - 1, length);
                buffer.AsSpan(length - carry, carry).CopyTo(buffer);
            }
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string? CleanEntityClassName(string? className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return null;
        }

        // Source2 types use a C prefix (CWeaponAK47). Do not strip names like C4.
        if (className.Length > 1 && className[0] == 'C' && char.IsUpper(className[1]))
        {
            return className[1..];
        }

        return className;
    }

    private static string? GetActiveWeaponName(CCSPlayerController player)
    {
        if (player.PlayerPawn is not CCSPlayerPawn pawn)
        {
            return null;
        }

        var weapon = pawn.ActiveWeapon;
        if (weapon == null)
        {
            return null;
        }

        return GetWeaponName(weapon);
    }

    private static string? GetWeaponName(CCSWeaponBase weapon)
    {
        var className = CleanEntityClassName(weapon.GetType().Name);
        var itemDefinitionIndex = GetWeaponItemDefinitionIndex(weapon);
        var itemDefinitionName = GetWeaponItemDefinitionName(weapon);
        var resolvedName = ResolveWeaponName(className, itemDefinitionIndex, itemDefinitionName);

        if (IsGenericKnifeName(className))
        {
            WriteAgentDebug(
                itemDefinitionIndex.HasValue || !string.IsNullOrWhiteSpace(itemDefinitionName) ? "H11" : "WATCHDOG_FAIL,H11",
                "DemoPlayback/CsDemoPlayback.cs:GetActiveWeaponName",
                itemDefinitionIndex.HasValue || !string.IsNullOrWhiteSpace(itemDefinitionName)
                    ? "weapon identity resolved from econ fields"
                    : "WATCHDOG_FAIL parser weapon issue: generic knife without econ identity",
                new
                {
                    className,
                    resolvedName,
                    itemDefinitionIndex,
                    itemDefinitionName,
                    rawType = weapon.GetType().FullName,
                    candidateFields = GetWeaponDebugFields(weapon),
                });
        }

        return resolvedName ?? className;
    }

    private static bool IsGenericKnifeName(string? className)
        => className != null
            && className.Contains("Knife", StringComparison.OrdinalIgnoreCase)
            && !className.Contains("Push", StringComparison.OrdinalIgnoreCase)
            && !className.Contains("Butterfly", StringComparison.OrdinalIgnoreCase)
            && !className.Contains("Karambit", StringComparison.OrdinalIgnoreCase)
            && !className.Contains("M9", StringComparison.OrdinalIgnoreCase)
            && !className.Contains("Tactical", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveWeaponName(string? className, int? itemDefinitionIndex, string? itemDefinitionName)
    {
        if (!string.IsNullOrWhiteSpace(itemDefinitionName))
        {
            return itemDefinitionName;
        }

        return itemDefinitionIndex switch
        {
            500 => "WeaponKnifeBayonet",
            503 => "WeaponKnifeClassic",
            505 => "WeaponKnifeFlip",
            506 => "WeaponKnifeGut",
            507 => "WeaponKnifeKarambit",
            508 => "WeaponKnifeM9Bayonet",
            509 => "WeaponKnifeTactical",
            512 => "WeaponKnifeFalchion",
            514 => "WeaponKnifeSurvivalBowie",
            515 => "WeaponKnifeButterfly",
            516 => "WeaponKnifePush",
            517 => "WeaponKnifeCord",
            518 => "WeaponKnifeCanis",
            519 => "WeaponKnifeUrsus",
            520 => "WeaponKnifeGypsyJackknife",
            521 => "WeaponKnifeOutdoor",
            522 => "WeaponKnifeStiletto",
            523 => "WeaponKnifeWidowmaker",
            525 => "WeaponKnifeSkeleton",
            _ => className,
        };
    }

    private static int? GetWeaponItemDefinitionIndex(object weapon)
    {
        foreach (var source in EnumerateWeaponDebugSources(weapon))
        {
            var value = GetIntProperty(source, -1, "ItemDefinitionIndex", "ItemDefinition", "DefinitionIndex", "DefIndex", "m_iItemDefinitionIndex");
            if (value >= 0)
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetWeaponItemDefinitionName(object weapon)
    {
        foreach (var source in EnumerateWeaponDebugSources(weapon))
        {
            foreach (var propertyName in new[] { "ItemDefinitionName", "DefinitionName", "ItemName", "Name", "DesignerName" })
            {
                if (GetPropertyObject(source, propertyName) is string { Length: > 0 } value)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static IEnumerable<object> EnumerateWeaponDebugSources(object weapon)
    {
        yield return weapon;

        foreach (var propertyName in new[] { "AttributeManager", "EconItemView", "Item", "ItemView", "ScriptItem" })
        {
            if (GetPropertyObject(weapon, propertyName) is { } source)
            {
                yield return source;

                foreach (var nestedPropertyName in new[] { "Item", "EconItemView", "ItemView" })
                {
                    if (GetPropertyObject(source, nestedPropertyName) is { } nestedSource)
                    {
                        yield return nestedSource;
                    }
                }
            }
        }
    }

    private static Dictionary<string, object?> GetWeaponDebugFields(object weapon)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var source in EnumerateWeaponDebugSources(weapon).Take(8))
        {
            var sourceName = source.GetType().Name;
            foreach (var propertyName in new[] { "ItemDefinitionIndex", "ItemDefinition", "DefinitionIndex", "DefIndex", "m_iItemDefinitionIndex", "ItemDefinitionName", "DefinitionName", "ItemName", "Name", "DesignerName" })
            {
                var value = GetPropertyObject(source, propertyName);
                if (value is string or int or uint or short or ushort or byte or sbyte)
                {
                    fields[$"{sourceName}.{propertyName}"] = value;
                }
            }
        }

        return fields;
    }

    private static void WriteAgentDebug(string hypothesisId, string location, string message, object data)
    {
        try
        {
            var payload = new
            {
                sessionId = "0ef808",
                runId = "watchdog",
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            File.AppendAllText(
                @"c:\Users\ayden\Documents\Github Projects\cs2 viewer\ValveResourceFormat\debug-0ef808.log",
                System.Text.Json.JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Debug instrumentation must not affect parsing.
        }
    }
}
