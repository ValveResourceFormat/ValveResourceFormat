using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat.DemoPlayback;

namespace GUI.Controls
{
    sealed class CsDemoTopHudControl : Control
    {
        private const int HeaderHeight = 76;
        private const int MapGap = 14;
        private const int MapSize = 288;
        private const int MapLabelHeight = 20;
        private const int MapPanelPadding = 8;
        private const int CollapsedHeight = 40;

        private readonly List<(Rectangle Rect, CsDemoPlayerInfo Player)> playerCards = [];
        private Rectangle hideButtonRect;
        private Rectangle showButtonRect;
        private Rectangle scorePillRect;
        private Rectangle mapPanelRect;
        private bool showHud = true;

        public CsDemoTopHudControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = GetExpandedHeight();
            Dock = DockStyle.Top;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        }

        public event Action<bool>? ShowHudChanged;
        public event Action<ulong>? PlayerClicked;

        public CsDemoSummary Summary { get; set; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, TimeSpan.Zero, [], [], [], false);

        public CsDemoFrame Frame { get; set; } = CsDemoFrame.Empty;

        public int RoundNumber { get; set; } = 1;

        public ulong? SelectedSteamId { get; set; }

        public RadarOverview? Radar { get; set; }

        public bool ShowHud
        {
            get => showHud;
            set
            {
                if (showHud == value)
                {
                    return;
                }

                showHud = value;
                Height = showHud ? GetExpandedHeight() : CollapsedHeight;
                Invalidate();
                ShowHudChanged?.Invoke(showHud);
            }
        }

        public static int GetExpandedHeight() => HeaderHeight + MapGap + MapSize + MapLabelHeight + (MapPanelPadding * 2);

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            HandleClick(e.Location);
        }

        public bool HandleClick(Point location)
        {
            if (showHud && hideButtonRect.Contains(location))
            {
                ShowHud = false;
                return true;
            }

            if (!showHud && showButtonRect.Contains(location))
            {
                ShowHud = true;
                return true;
            }

            if (showHud)
            {
                foreach (var (rect, player) in playerCards)
                {
                    if (!rect.Contains(location))
                    {
                        continue;
                    }

                    PlayerClicked?.Invoke(player.SteamId);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the point lies in an opaque HUD region (score pill, map panel,
        /// or hide/show button). Used so clicks on the HUD don't pass through to the 3D viewport.
        /// </summary>
        public bool IsBlockingClick(Point location)
        {
            if (!showHud)
            {
                return showButtonRect.Contains(location);
            }

            return scorePillRect.Contains(location)
                || mapPanelRect.Contains(location)
                || hideButtonRect.Contains(location);
        }

        public void PaintHud(SKCanvas canvas, int width, int height)
        {
            playerCards.Clear();

            if (!showHud)
            {
                showButtonRect = new Rectangle(width - 122, 10, 108, 30);
                CsDemoHudSkiaHelpers.FillRoundRect(canvas, CsDemoHudSkiaHelpers.ToSkRect(showButtonRect), CsDemoHudSkiaHelpers.Rgba(14, 16, 22, 225), 6);
                CsDemoHudSkiaHelpers.DrawRoundRect(canvas, CsDemoHudSkiaHelpers.ToSkRect(showButtonRect), CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 22), 6);
                CsDemoHudSkiaHelpers.DrawCenteredText(canvas, "SHOW HUD", CsDemoHudSkiaHelpers.ToSkRect(showButtonRect), 11f, CsDemoHudSkiaHelpers.Rgba(225, 230, 238, 235));
                return;
            }

            var headerRect = new SKRect(0, 0, width, HeaderHeight);
            CsDemoHudSkiaHelpers.FillVerticalGradient(
                canvas,
                headerRect,
                CsDemoHudSkiaHelpers.Rgba(8, 10, 14, 200),
                CsDemoHudSkiaHelpers.Rgba(8, 10, 14, 0));

            DrawScoreStrip(canvas, width);

            hideButtonRect = new Rectangle(width - 102, 14, 88, 32);
            CsDemoHudSkiaHelpers.FillRoundRect(canvas, CsDemoHudSkiaHelpers.ToSkRect(hideButtonRect), CsDemoHudSkiaHelpers.Rgba(14, 16, 22, 225), 6);
            CsDemoHudSkiaHelpers.DrawRoundRect(canvas, CsDemoHudSkiaHelpers.ToSkRect(hideButtonRect), CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 22), 6);
            CsDemoHudSkiaHelpers.DrawCenteredText(canvas, "HIDE", CsDemoHudSkiaHelpers.ToSkRect(hideButtonRect), 11f, CsDemoHudSkiaHelpers.Rgba(225, 230, 238, 235));
        }

        /// <summary>
        /// Paints the minimap panel. Drawn separately (after the rest of the HUD, including the
        /// bottom bar) so it stays on the topmost UI layer.
        /// </summary>
        public void PaintMapOverlay(SKCanvas canvas)
        {
            if (!showHud)
            {
                mapPanelRect = Rectangle.Empty;
                return;
            }

            DrawMapPanel(canvas);
        }

        private void DrawMapPanel(SKCanvas canvas)
        {
            var panelLeft = 12;
            var panelTop = HeaderHeight + MapGap;
            var panelWidth = MapSize + (MapPanelPadding * 2);
            var panelHeight = MapSize + MapLabelHeight + (MapPanelPadding * 2);
            mapPanelRect = new Rectangle(panelLeft, panelTop, panelWidth, panelHeight);
            var panelRect = CsDemoHudSkiaHelpers.ToSkRect(mapPanelRect);

            CsDemoHudSkiaHelpers.FillRoundRect(canvas, panelRect, CsDemoHudSkiaHelpers.Rgba(14, 16, 22, 225), 8);
            CsDemoHudSkiaHelpers.DrawRoundRect(canvas, panelRect, CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 26), 8);

            var mapLeft = panelLeft + MapPanelPadding;
            var mapTop = panelTop + MapPanelPadding;
            var mapRect = new SKRect(mapLeft, mapTop, mapLeft + MapSize, mapTop + MapSize);

            if (Radar != null)
            {
                DrawRadarMap(canvas, mapRect);
            }
            else
            {
                DrawGridFallback(canvas, mapRect);
            }

            var labelRect = new SKRect(mapLeft, mapTop + MapSize, mapLeft + MapSize, mapTop + MapSize + MapLabelHeight);
            CsDemoHudSkiaHelpers.DrawCenteredText(canvas, Summary.MapName, labelRect, 10f, CsDemoHudSkiaHelpers.Rgba(180, 188, 200, 220));
        }

        private void DrawRadarMap(SKCanvas canvas, SKRect mapRect)
        {
            CsDemoHudSkiaHelpers.DrawBitmapClipped(canvas, Radar!.Image, mapRect, 5);
            CsDemoHudSkiaHelpers.DrawRoundRect(canvas, mapRect, CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 28), 5);

            const float dotRadius = 6f;
            foreach (var player in Frame.Players.Where(static player => player.IsAlive))
            {
                var (fx, fy) = Radar!.WorldToFraction(player.Position);
                var cx = mapRect.Left + (fx * mapRect.Width);
                var cy = mapRect.Top + (fy * mapRect.Height);
                var dotColor = player.Team == CsDemoTeam.CounterTerrorist
                    ? new SKColor(0, 191, 255)
                    : new SKColor(218, 165, 32);

                using var fillPaint = new SKPaint { Color = dotColor, IsAntialias = true, Style = SKPaintStyle.Fill };
                using var strokePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
                canvas.DrawCircle(cx, cy, dotRadius, fillPaint);
                canvas.DrawCircle(cx, cy, dotRadius, strokePaint);
            }
        }

        private void DrawGridFallback(SKCanvas canvas, SKRect mapRect)
        {
            CsDemoHudSkiaHelpers.FillGlassRect(
                canvas,
                mapRect,
                CsDemoHudSkiaHelpers.Rgba(42, 44, 50, 130),
                CsDemoHudSkiaHelpers.Rgba(88, 88, 82, 80),
                5);

            using var gridPaint = new SKPaint
            {
                Color = CsDemoHudSkiaHelpers.Rgba(190, 205, 210, 80),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
            };

            for (var x = mapRect.Left + 20; x < mapRect.Right; x += 28)
            {
                canvas.DrawLine(x, mapRect.Top + 6, x, mapRect.Bottom - 22, gridPaint);
            }

            for (var y = mapRect.Top + 18; y < mapRect.Bottom - 22; y += 24)
            {
                canvas.DrawLine(mapRect.Left + 8, y, mapRect.Right - 8, y, gridPaint);
            }

            var players = Frame.Players.Where(static player => player.IsAlive).ToArray();
            if (players.Length > 0)
            {
                var minX = players.Min(static player => player.Position.X);
                var maxX = players.Max(static player => player.Position.X);
                var minY = players.Min(static player => player.Position.Y);
                var maxY = players.Max(static player => player.Position.Y);

                const float dotRadius = 6f;
                foreach (var player in players)
                {
                    var px = Normalize(player.Position.X, minX, maxX);
                    var py = Normalize(player.Position.Y, minY, maxY);
                    var cx = mapRect.Left + 14 + (px * (mapRect.Width - 28));
                    var cy = mapRect.Top + 14 + ((1f - py) * (mapRect.Height - 40));
                    var dotColor = player.Team == CsDemoTeam.CounterTerrorist
                        ? new SKColor(0, 191, 255)
                        : new SKColor(218, 165, 32);

                    using var fillPaint = new SKPaint { Color = dotColor, IsAntialias = true, Style = SKPaintStyle.Fill };
                    using var strokePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
                    canvas.DrawCircle(cx, cy, dotRadius, fillPaint);
                    canvas.DrawCircle(cx, cy, dotRadius, strokePaint);
                }
            }
        }

        private void DrawScoreStrip(SKCanvas canvas, int width)
        {
            var center = width / 2;

            var tPlayers = Summary.Players.Where(static player => player.Team == CsDemoTeam.Terrorist).Take(5).ToArray();
            var ctPlayers = Summary.Players.Where(static player => player.Team == CsDemoTeam.CounterTerrorist).Take(5).ToArray();

            var tAlive = tPlayers.Count(player => Frame.Players.Any(frame => frame.SteamId == player.SteamId && frame.IsAlive));
            var ctAlive = ctPlayers.Count(player => Frame.Players.Any(frame => frame.SteamId == player.SteamId && frame.IsAlive));

            var tColor = CsDemoHudSkiaHelpers.Rgba(232, 168, 56, 255);
            var ctColor = CsDemoHudSkiaHelpers.Rgba(82, 168, 235, 255);

            // Unified scoreboard pill: [ T count | timer + round | CT count ].
            const int countWidth = 76;
            const int timerWidth = 184;
            const int pillTop = 10;
            const int pillBottom = 64;
            var pillWidth = countWidth + timerWidth + countWidth;
            var pillLeft = center - (pillWidth / 2);
            scorePillRect = new Rectangle(pillLeft, pillTop, pillWidth, pillBottom - pillTop);
            var pillRect = CsDemoHudSkiaHelpers.ToSkRect(scorePillRect);

            CsDemoHudSkiaHelpers.FillRoundRect(canvas, pillRect, CsDemoHudSkiaHelpers.Rgba(14, 16, 22, 230), 7);
            CsDemoHudSkiaHelpers.DrawRoundRect(canvas, pillRect, CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 24), 7);

            // T accent strip + count.
            var tStripRect = new SKRect(pillLeft + 2, pillTop + 2, pillLeft + countWidth - 1, pillTop + 5);
            CsDemoHudSkiaHelpers.FillRoundRect(canvas, tStripRect, tColor, 2f);
            var tNumberRect = new SKRect(pillLeft, pillTop + 6, pillLeft + countWidth, pillBottom - 18);
            CsDemoHudSkiaHelpers.DrawCenteredText(canvas, tAlive.ToString(CultureInfo.InvariantCulture), tNumberRect, 24f, tColor);
            CsDemoHudSkiaHelpers.DrawCenteredText(canvas, "T", new SKRect(pillLeft, pillBottom - 20, pillLeft + countWidth, pillBottom - 4), 10f, tColor.WithAlpha(210));

            // Middle: timer + round.
            var elapsed = TimeSpan.FromSeconds(Frame.Tick / (double)Math.Max(1, CsDemoPlayback.TickRate));
            var timerRect = new SKRect(pillLeft + countWidth, pillTop + 6, pillLeft + countWidth + timerWidth, pillBottom - 20);
            CsDemoHudSkiaHelpers.DrawCenteredText(canvas, FormatDuration(elapsed), timerRect, 22f, CsDemoHudSkiaHelpers.Rgba(232, 88, 100, 255));
            CsDemoHudSkiaHelpers.DrawCenteredText(
                canvas,
                $"ROUND {RoundNumber}",
                new SKRect(pillLeft + countWidth, pillBottom - 20, pillLeft + countWidth + timerWidth, pillBottom - 4),
                10f,
                CsDemoHudSkiaHelpers.Rgba(180, 188, 200, 225));

            // CT accent strip + count.
            var ctSectionLeft = pillLeft + countWidth + timerWidth;
            var ctStripRect = new SKRect(ctSectionLeft + 1, pillTop + 2, ctSectionLeft + countWidth - 2, pillTop + 5);
            CsDemoHudSkiaHelpers.FillRoundRect(canvas, ctStripRect, ctColor, 2f);
            var ctNumberRect = new SKRect(ctSectionLeft, pillTop + 6, ctSectionLeft + countWidth, pillBottom - 18);
            CsDemoHudSkiaHelpers.DrawCenteredText(canvas, ctAlive.ToString(CultureInfo.InvariantCulture), ctNumberRect, 24f, ctColor);
            CsDemoHudSkiaHelpers.DrawCenteredText(canvas, "CT", new SKRect(ctSectionLeft, pillBottom - 20, ctSectionLeft + countWidth, pillBottom - 4), 10f, ctColor.WithAlpha(210));

            // Hairline dividers between sections.
            using (var dividerPaint = new SKPaint { Color = CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 22), StrokeWidth = 1f, IsAntialias = true })
            {
                canvas.DrawLine(pillLeft + countWidth, pillTop + 10, pillLeft + countWidth, pillBottom - 10, dividerPaint);
                canvas.DrawLine(ctSectionLeft, pillTop + 10, ctSectionLeft, pillBottom - 10, dividerPaint);
            }

            const int cardWidth = 54;
            const int gap = 6;
            var teamWidth = 5 * cardWidth + 4 * gap;
            DrawTeamCards(canvas, tPlayers, pillLeft - 16 - teamWidth, cardWidth, gap, tColor);
            DrawTeamCards(canvas, ctPlayers, pillLeft + pillWidth + 16, cardWidth, gap, ctColor);
        }

        private void DrawTeamCards(SKCanvas canvas, CsDemoPlayerInfo[] players, int x, int cardWidth, int gap, SKColor color)
        {
            for (var i = 0; i < players.Length; i++)
            {
                var rect = new Rectangle(x + i * (cardWidth + gap), 10, cardWidth, 54);
                var alive = Frame.Players.Any(framePlayer => framePlayer.SteamId == players[i].SteamId && framePlayer.IsAlive);
                var selected = SelectedSteamId == players[i].SteamId;
                playerCards.Add((rect, players[i]));

                var skRect = CsDemoHudSkiaHelpers.ToSkRect(rect);
                CsDemoHudSkiaHelpers.FillRoundRect(
                    canvas,
                    skRect,
                    CsDemoHudSkiaHelpers.Rgba(14, 16, 22, (byte)(alive ? 225 : 140)),
                    6);

                // Thin accent strip across the top, in team color.
                var accentRect = new SKRect(skRect.Left + 2, skRect.Top + 2, skRect.Right - 2, skRect.Top + 5);
                using (var accentPaint = new SKPaint { Color = color.WithAlpha((byte)(alive ? 230 : 100)), IsAntialias = true, Style = SKPaintStyle.Fill })
                {
                    canvas.DrawRoundRect(accentRect, 1.5f, 1.5f, accentPaint);
                }

                CsDemoHudSkiaHelpers.DrawRoundRect(
                    canvas,
                    skRect,
                    selected ? CsDemoHudSkiaHelpers.Rgba(245, 247, 250, 240) : CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 22),
                    6,
                    selected ? 1.8f : 1f);

                var name = players[i].Name.Length > 7 ? players[i].Name[..7] : players[i].Name;
                CsDemoHudSkiaHelpers.DrawCenteredText(
                    canvas,
                    name,
                    new SKRect(rect.Left + 2, rect.Top + 10, rect.Right - 2, rect.Top + 32),
                    10f,
                    alive ? CsDemoHudSkiaHelpers.Rgba(240, 244, 250, 240) : CsDemoHudSkiaHelpers.Rgba(150, 158, 168, 200));
                CsDemoHudSkiaHelpers.DrawCenteredText(
                    canvas,
                    alive ? "$--" : "DEAD",
                    new SKRect(rect.Left + 2, rect.Top + 32, rect.Right - 2, rect.Bottom - 4),
                    8f,
                    alive ? CsDemoHudSkiaHelpers.Rgba(170, 178, 190, 225) : CsDemoHudSkiaHelpers.Rgba(150, 158, 168, 200));
            }
        }

        private static float Normalize(float value, float min, float max)
        {
            return max <= min ? 0.5f : Math.Clamp((value - min) / (max - min), 0f, 1f);
        }

        private static string FormatDuration(TimeSpan time)
        {
            return time.TotalHours >= 1
                ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }

    sealed class CsDemoBottomHudControl : Control
    {
        private readonly List<(Rectangle Rect, Action Action)> actions = [];
        private readonly List<(Rectangle Rect, CsDemoTimelineEvent TimelineEvent)> timelineEventMarkers = [];
        private readonly object hitTestLock = new();
        private readonly float[] speeds = [0.25f, 0.5f, 1f, 2f, 4f, 8f];
        private Rectangle voxelButtonRect;
        private Rectangle animDebugButtonRect;
        private Rectangle timelineRect;
        private CsDemoTimelineEvent? hoveredTimelineEvent;

        public const int PreferredHeight = 212;

        public CsDemoBottomHudControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = PreferredHeight;
            Dock = DockStyle.Bottom;
            Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        }

        /// <summary>Always true within the bar — clicks anywhere here are consumed by the HUD.</summary>
        public bool IsBlockingClick(Point location)
        {
            return location.Y >= 0 && location.Y < Height && location.X >= 0 && location.X < Width;
        }

        public event Action? TogglePlaybackRequested;
        public event Action<int>? SkipSecondsRequested;
        public event Action<int>? SkipRoundRequested;
        public event Action<float>? SpeedRequested;
        public event Action<int>? SeekTickRequested;
        public event Action<bool>? VoxelBoxChanged;
        public event Action<bool>? AnimDebugChanged;

        public CsDemoSummary Summary { get; set; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, TimeSpan.Zero, [], [], [], false);

        public CsDemoFrame Frame { get; set; } = CsDemoFrame.Empty;

        public bool Playing { get; set; }

        public float Speed { get; set; } = 1f;

        public int RoundNumber { get; set; } = 1;

        public ulong? SelectedSteamId { get; set; }

        public string CameraModeLabel { get; set; } = "FREE CAM";

        public SKBitmap? DeathMarkerImage { get; set; }

        public SKBitmap? KillMarkerImage { get; set; }

        public bool VoxelBoxEnabled { get; set; }

        public bool AnimDebugEnabled { get; set; }

        public string DebugSignature => string.Create(
            CultureInfo.InvariantCulture,
            $"{VoxelBoxEnabled}|{AnimDebugEnabled}");

        public string HoverSignature
        {
            get
            {
                var hover = hoveredTimelineEvent;
                return hover == null
                    ? string.Empty
                    : $"{hover.Tick}:{hover.PlayerSteamId}:{hover.OtherPlayerSteamId}";
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            HandleClick(e.Location);
        }

        public bool HandleClick(Point location)
        {
            (Rectangle Rect, CsDemoTimelineEvent TimelineEvent)[] markerSnapshot;
            (Rectangle Rect, Action Action)[] actionSnapshot;

            lock (hitTestLock)
            {
                markerSnapshot = timelineEventMarkers.ToArray();
                actionSnapshot = actions.ToArray();
            }

            if (voxelButtonRect.Contains(location))
            {
                VoxelBoxEnabled = !VoxelBoxEnabled;
                VoxelBoxChanged?.Invoke(VoxelBoxEnabled);
                Invalidate();
                return true;
            }

            if (animDebugButtonRect.Contains(location))
            {
                AnimDebugEnabled = !AnimDebugEnabled;
                AnimDebugChanged?.Invoke(AnimDebugEnabled);
                Invalidate();
                return true;
            }

            foreach (var (rect, timelineEvent) in markerSnapshot)
            {
                if (!rect.Contains(location))
                {
                    continue;
                }

                SeekTickRequested?.Invoke(Math.Max(0, timelineEvent.Tick - (CsDemoPlayback.TickRate * 3)));
                return true;
            }

            if (timelineRect.Contains(location) && Summary.TickCount > 0)
            {
                var percent = Math.Clamp((location.X - timelineRect.Left) / (float)Math.Max(1, timelineRect.Width), 0f, 1f);
                SeekTickRequested?.Invoke((int)(percent * Summary.TickCount));
                return true;
            }

            foreach (var action in actionSnapshot)
            {
                if (action.Rect.Contains(location))
                {
                    action.Action();
                    return true;
                }
            }

            return false;
        }

        public bool HandleHover(Point? location)
        {
            CsDemoTimelineEvent? nextHover = null;

            if (location != null)
            {
                (Rectangle Rect, CsDemoTimelineEvent TimelineEvent)[] markerSnapshot;

                lock (hitTestLock)
                {
                    markerSnapshot = timelineEventMarkers.ToArray();
                }

                foreach (var (rect, timelineEvent) in markerSnapshot)
                {
                    if (rect.Contains(location.Value))
                    {
                        nextHover = timelineEvent;
                        break;
                    }
                }
            }

            if (hoveredTimelineEvent == nextHover)
            {
                return false;
            }

            hoveredTimelineEvent = nextHover;
            return true;
        }

        public void PaintHud(SKCanvas canvas, int width, int height)
        {
            lock (hitTestLock)
            {
                actions.Clear();
                timelineEventMarkers.Clear();
            }

            var bounds = new SKRect(0, 0, width, height);
            CsDemoHudSkiaHelpers.FillVerticalGradient(
                canvas,
                bounds,
                CsDemoHudSkiaHelpers.Rgba(8, 10, 14, 0),
                CsDemoHudSkiaHelpers.Rgba(8, 10, 14, 230));

            // Hairline divider just above the control row to anchor the surface.
            using (var hairlinePaint = new SKPaint { Color = CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 18), StrokeWidth = 1f, IsAntialias = false })
            {
                canvas.DrawLine(16, 138, width - 16, 138, hairlinePaint);
            }

            DrawTimeline(canvas, width);
            DrawDebugButtons(canvas, width);
            DrawControls(canvas, width);
        }

        private void DrawDebugButtons(SKCanvas canvas, int width)
        {
            var y = 52;
            var x = width - 16 - 72;
            voxelButtonRect = new Rectangle(x, y, 72, 30);
            DrawDebugButton(canvas, voxelButtonRect, "VOXEL", VoxelBoxEnabled);

            x -= 8 + 72;
            animDebugButtonRect = new Rectangle(x, y, 72, 30);
            DrawDebugButton(canvas, animDebugButtonRect, "ANIM", AnimDebugEnabled);
        }

        private static void DrawDebugButton(SKCanvas canvas, Rectangle rect, string text, bool active)
        {
            var skRect = CsDemoHudSkiaHelpers.ToSkRect(rect);
            if (active)
            {
                CsDemoHudSkiaHelpers.FillRoundRect(canvas, skRect, CsDemoHudSkiaHelpers.Rgba(240, 244, 250, 230), 5);
            }
            else
            {
                CsDemoHudSkiaHelpers.FillRoundRect(canvas, skRect, CsDemoHudSkiaHelpers.Rgba(20, 24, 30, 225), 5);
                CsDemoHudSkiaHelpers.DrawRoundRect(canvas, skRect, CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 22), 5);
            }

            CsDemoHudSkiaHelpers.DrawCenteredText(
                canvas,
                text,
                skRect,
                11f,
                active ? CsDemoHudSkiaHelpers.Rgba(12, 14, 20, 240) : CsDemoHudSkiaHelpers.Rgba(225, 230, 238, 240));
        }

        private void DrawTimeline(SKCanvas canvas, int width)
        {
            timelineRect = new Rectangle(16, 100, width - 32, 24);
            CsDemoHudSkiaHelpers.FillRoundRect(canvas, CsDemoHudSkiaHelpers.ToSkRect(timelineRect), CsDemoHudSkiaHelpers.Rgba(22, 26, 34, 230), 4);

            var rounds = Summary.Rounds.Count > 0
                ? Summary.Rounds
                : [new CsDemoRoundSegment(1, 0, Math.Max(1, Summary.TickCount))];

            foreach (var round in rounds)
            {
                var left = TickToX(round.StartTick);
                var right = TickToX(round.EndTick);
                var roundWidth = Math.Max(2, right - left);
                var color = round.RoundNumber % 2 == 0
                    ? CsDemoHudSkiaHelpers.Rgba(120, 130, 145, 200)
                    : CsDemoHudSkiaHelpers.Rgba(70, 78, 92, 200);
                CsDemoHudSkiaHelpers.FillRoundRect(
                    canvas,
                    new SKRect(left, timelineRect.Top, left + roundWidth, timelineRect.Bottom),
                    color,
                    0);
            }

            var laneEndTicks = Enumerable.Repeat(int.MinValue / 2, 3).ToArray();
            foreach (var timelineEvent in Summary.TimelineEvents.Where(ShouldDrawTimelineEvent).OrderBy(static timelineEvent => timelineEvent.Tick))
            {
                var x = TickToX(timelineEvent.Tick);
                var hovered = hoveredTimelineEvent == timelineEvent;
                var markerSize = hovered ? 30 : 22;
                var lane = GetMarkerLane(timelineEvent.Tick, laneEndTicks);
                var rect = new Rectangle(
                    x - (markerSize / 2),
                    timelineRect.Top - markerSize - 4 - (lane * 23) - (hovered ? 6 : 0),
                    markerSize,
                    markerSize);
                lock (hitTestLock)
                {
                    timelineEventMarkers.Add((Rectangle.Inflate(rect, 8, 18), timelineEvent));
                }

                DrawTimelineEventLabel(canvas, width, x, timelineEvent, hovered);
                DrawTimelineEventImage(canvas, CsDemoHudSkiaHelpers.ToSkRect(rect), timelineEvent, hovered);
            }

            var scrubX = TickToX(Frame.Tick);
            using var scrubPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, StrokeWidth = 2.5f, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(scrubX, timelineRect.Top - 4, scrubX, timelineRect.Bottom + 4, scrubPaint);
            using var scrubDotPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawCircle(scrubX, timelineRect.Top + (timelineRect.Height / 2f), 5f, scrubDotPaint);
        }

        private bool ShouldDrawTimelineEvent(CsDemoTimelineEvent timelineEvent)
        {
            if (timelineEvent.Kind == CsDemoTimelineEventKind.Bomb)
            {
                return false;
            }

            if (SelectedSteamId == null)
            {
                return false;
            }

            return timelineEvent.PlayerSteamId == SelectedSteamId || timelineEvent.OtherPlayerSteamId == SelectedSteamId;
        }

        private static int GetMarkerLane(int tick, int[] laneEndTicks)
        {
            var minGapTicks = CsDemoPlayback.TickRate * 4;

            for (var lane = 0; lane < laneEndTicks.Length; lane++)
            {
                if (tick - laneEndTicks[lane] >= minGapTicks)
                {
                    laneEndTicks[lane] = tick;
                    return lane;
                }
            }

            var fallbackLane = Array.IndexOf(laneEndTicks, laneEndTicks.Min());
            laneEndTicks[fallbackLane] = tick;
            return fallbackLane;
        }

        private void DrawTimelineEventLabel(SKCanvas canvas, int width, int markerX, CsDemoTimelineEvent timelineEvent, bool hovered)
        {
            if (!hovered)
            {
                return;
            }

            var attacker = string.IsNullOrWhiteSpace(timelineEvent.OtherPlayerName) ? "Unknown" : timelineEvent.OtherPlayerName;
            var victim = string.IsNullOrWhiteSpace(timelineEvent.PlayerName) ? "Unknown" : timelineEvent.PlayerName;
            var text = $"{attacker} -> {victim}";
            const int labelWidth = 190;
            var rect = new SKRect(
                Math.Clamp(markerX - (labelWidth / 2), 10, Math.Max(10, width - labelWidth - 10)),
                2,
                Math.Clamp(markerX - (labelWidth / 2), 10, Math.Max(10, width - labelWidth - 10)) + labelWidth,
                24);

            CsDemoHudSkiaHelpers.FillRoundRect(canvas, rect, CsDemoHudSkiaHelpers.Rgba(16, 18, 24, 235), 4);
            CsDemoHudSkiaHelpers.DrawCenteredText(canvas, text, rect, 9f, SKColors.White);
        }

        private void DrawTimelineEventImage(SKCanvas canvas, SKRect rect, CsDemoTimelineEvent timelineEvent, bool hovered)
        {
            using var glowPaint = new SKPaint
            {
                Color = CsDemoHudSkiaHelpers.Rgba(255, 255, 255, hovered ? (byte)130 : (byte)70),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            var glowRect = new SKRect(
                rect.Left - (hovered ? 8 : 5),
                rect.Top - (hovered ? 8 : 5),
                rect.Right + (hovered ? 8 : 5),
                rect.Bottom + (hovered ? 8 : 5));
            canvas.DrawOval(glowRect, glowPaint);

            var image = timelineEvent.PlayerSteamId == SelectedSteamId ? DeathMarkerImage : KillMarkerImage;
            CsDemoHudSkiaHelpers.DrawBitmap(canvas, image, rect);
        }

        private void DrawControls(SKCanvas canvas, int width)
        {
            var y = 148;
            var x = 16;

            AddButton(canvas, ref x, y, 78, Playing ? "II" : "PLAY", () => TogglePlaybackRequested?.Invoke());

            var elapsed = TimeSpan.FromSeconds(Frame.Tick / (double)Math.Max(1, CsDemoPlayback.TickRate));
            var timeText = $"{FormatDuration(elapsed)} / {FormatDuration(Summary.Duration)}";
            CsDemoHudSkiaHelpers.DrawText(
                canvas,
                timeText,
                new SKRect(x + 8, y, x + 180, y + 40),
                12f,
                CsDemoHudSkiaHelpers.Rgba(230, 234, 240, 240),
                SKTextAlign.Left);
            x += 184;

            AddButton(canvas, ref x, y, 68, "-15s", () => SkipSecondsRequested?.Invoke(-15));
            AddButton(canvas, ref x, y, 68, "+15s", () => SkipSecondsRequested?.Invoke(15));
            AddButton(canvas, ref x, y, 56, "|<", () => SkipRoundRequested?.Invoke(-1));
            CsDemoHudSkiaHelpers.DrawText(
                canvas,
                $"Round {RoundNumber}",
                new SKRect(x + 8, y, x + 116, y + 40),
                12f,
                CsDemoHudSkiaHelpers.Rgba(180, 188, 200, 235),
                SKTextAlign.Left);
            x += 120;
            AddButton(canvas, ref x, y, 56, ">|", () => SkipRoundRequested?.Invoke(1));

            x += 18;
            foreach (var candidate in speeds)
            {
                var active = Math.Abs(candidate - Speed) < 0.01f;
                AddButton(canvas, ref x, y, 58, FormatSpeed(candidate), () => SpeedRequested?.Invoke(candidate), active);
            }

            CsDemoHudSkiaHelpers.DrawText(
                canvas,
                $"View: {CameraModeLabel}    Click player: first / third / free    Left-click view: free look",
                new SKRect(18, 194, width - 18, 210),
                10f,
                CsDemoHudSkiaHelpers.Rgba(150, 158, 170, 215),
                SKTextAlign.Left,
                bold: false);
        }

        private int TickToX(int tick)
        {
            if (Summary.TickCount <= 0)
            {
                return timelineRect.Left;
            }

            var percent = Math.Clamp(tick / (float)Summary.TickCount, 0f, 1f);
            return timelineRect.Left + (int)(percent * timelineRect.Width);
        }

        private void AddButton(SKCanvas canvas, ref int x, int y, int width, string text, Action action, bool active = false)
        {
            var rect = new Rectangle(x, y, width, 40);
            lock (hitTestLock)
            {
                actions.Add((rect, action));
            }

            var skRect = CsDemoHudSkiaHelpers.ToSkRect(rect);
            if (active)
            {
                CsDemoHudSkiaHelpers.FillRoundRect(canvas, skRect, CsDemoHudSkiaHelpers.Rgba(240, 244, 250, 230), 5);
            }
            else
            {
                CsDemoHudSkiaHelpers.FillRoundRect(canvas, skRect, CsDemoHudSkiaHelpers.Rgba(20, 24, 30, 225), 5);
                CsDemoHudSkiaHelpers.DrawRoundRect(canvas, skRect, CsDemoHudSkiaHelpers.Rgba(255, 255, 255, 22), 5);
            }

            CsDemoHudSkiaHelpers.DrawCenteredText(
                canvas,
                text,
                skRect,
                12f,
                active ? CsDemoHudSkiaHelpers.Rgba(12, 14, 20, 240) : CsDemoHudSkiaHelpers.Rgba(230, 234, 240, 240));
            x += width + 8;
        }

        private static string FormatSpeed(float speed)
        {
            return speed switch
            {
                0.25f => ".25x",
                0.5f => ".5x",
                _ => speed.ToString("0x", CultureInfo.InvariantCulture),
            };
        }

        private static string FormatDuration(TimeSpan time)
        {
            return time.TotalHours >= 1
                ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }
}
