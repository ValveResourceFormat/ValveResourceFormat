using System.Globalization;
using System.Linq;
using System.Numerics;
using GUI.Controls;
using SkiaSharp;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers;

static class CsDemoAnimDebugOverlay
{
    private const float HeadOffsetZ = 86f;
    private const float NearDistance = 500f;
    private const float MidDistance = 1500f;
    private const float ScreenMargin = 8f;

    public static int ResolveDetailLevel(float distance, bool isSelected)
    {
        if (isSelected)
        {
            return 2;
        }

        if (distance < NearDistance)
        {
            return 2;
        }

        return distance < MidDistance ? 1 : 0;
    }

    public static bool TryProject(
        Vector3 worldPosition,
        Matrix4x4 viewProjectionMatrix,
        int width,
        int height,
        out Vector2 screenPosition,
        out float distance)
    {
        screenPosition = default;
        distance = 0f;

        var clip = Vector4.Transform(new Vector4(worldPosition, 1f), viewProjectionMatrix);
        if (clip.W <= 0.001f)
        {
            return false;
        }

        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        if (ndcX is < -1.1f or > 1.1f || ndcY is < -1.1f or > 1.1f)
        {
            return false;
        }

        screenPosition = new Vector2(
            (ndcX * 0.5f + 0.5f) * width,
            (1f - (ndcY * 0.5f + 0.5f)) * height);
        distance = clip.W;
        return true;
    }

    public static void Draw(
        SKCanvas canvas,
        Camera camera,
        int width,
        int height,
        IEnumerable<(PlayerAnimDebugData Data, float Alpha)> panels)
    {
        var projected = new List<(PlayerAnimDebugData Data, float Alpha, Vector2 Screen, float Distance)>();

        foreach (var (data, alpha) in panels)
        {
            var headPosition = data.WorldPosition + new Vector3(0f, 0f, HeadOffsetZ);
            if (!TryProject(headPosition, camera.ViewProjectionMatrix, width, height, out var screen, out var distance))
            {
                continue;
            }

            projected.Add((data, alpha, screen, distance));
        }

        projected.Sort(static (left, right) => right.Distance.CompareTo(left.Distance));

        var occupiedRects = new List<SKRect>();
        foreach (var (data, alpha, screen, distance) in projected)
        {
            var detailLevel = ResolveDetailLevel(distance, data.IsSelected);
            var panelRect = CreatePanelRect(data, screen, detailLevel);
            panelRect = ClampToScreen(panelRect, width, height);
            panelRect = OffsetOverlapping(panelRect, occupiedRects);
            occupiedRects.Add(panelRect);
            DrawPanel(canvas, data, alpha, panelRect, detailLevel);
        }
    }

    private static float ResolveFontSize(int detailLevel) => detailLevel switch
    {
        0 => 13f,
        1 => 15f,
        _ => 17f,
    };

    private static SKRect CreatePanelRect(PlayerAnimDebugData data, Vector2 screen, int detailLevel)
    {
        var lines = BuildLines(data, detailLevel);
        var fontSize = ResolveFontSize(detailLevel);
        var lineHeight = fontSize + 3f;
        var paddingY = 6f;
        var panelWidth = ResolvePanelWidth(data, detailLevel, lines);
        var panelHeight = paddingY * 2 + (lines.Count * lineHeight);
        var left = screen.X - (panelWidth * 0.5f);
        var top = screen.Y - panelHeight - 12f;
        return new SKRect(left, top, left + panelWidth, top + panelHeight);
    }

    private static void DrawPanel(
        SKCanvas canvas,
        PlayerAnimDebugData data,
        float alpha,
        SKRect rect,
        int detailLevel)
    {
        var lines = BuildLines(data, detailLevel);
        var fontSize = ResolveFontSize(detailLevel);
        var lineHeight = fontSize + 3f;
        var paddingX = 8f;
        var paddingY = 6f;
        var panelAlpha = (byte)Math.Clamp((int)(220 * alpha), 0, 220);

        CsDemoHudSkiaHelpers.FillGlassRect(
            canvas,
            rect,
            CsDemoHudSkiaHelpers.Rgba(18, 22, 30, panelAlpha),
            CsDemoHudSkiaHelpers.Rgba(10, 12, 18, (byte)Math.Clamp(panelAlpha - 40, 0, 255)),
            6);
        CsDemoHudSkiaHelpers.DrawRoundRect(
            canvas,
            rect,
            data.IsSelected
                ? CsDemoHudSkiaHelpers.Rgba(255, 255, 255, (byte)Math.Clamp((int)(180 * alpha), 0, 180))
                : CsDemoHudSkiaHelpers.Rgba(120, 140, 160, (byte)Math.Clamp((int)(120 * alpha), 0, 120)),
            6,
            data.IsSelected ? 1.5f : 1f);

        var textAlpha = (byte)Math.Clamp((int)(255 * alpha), 0, 255);
        var textColor = CsDemoHudSkiaHelpers.Rgba(240, 245, 250, textAlpha);
        var mutedColor = CsDemoHudSkiaHelpers.Rgba(180, 190, 200, textAlpha);
        var warningColor = CsDemoHudSkiaHelpers.Rgba(255, 196, 96, textAlpha);

        var y = rect.Top + paddingY + fontSize;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var color = line.IsWarning ? warningColor : i == 0 ? textColor : mutedColor;
            var lineRect = new SKRect(rect.Left + paddingX, y - fontSize, rect.Right - paddingX, y + 2f);
            CsDemoHudSkiaHelpers.DrawText(canvas, line.Text, lineRect, fontSize, color, SKTextAlign.Left, bold: i == 0);
            y += lineHeight;
        }
    }

    private static List<(string Text, bool IsWarning)> BuildLines(PlayerAnimDebugData data, int detailLevel)
    {
        var lines = new List<(string Text, bool IsWarning)>();
        var name = data.Name.Length > 14 ? data.Name[..14] : data.Name;
        lines.Add(($"{name} #{data.Slot}", false));

        var stateLine = string.IsNullOrEmpty(data.EventLabel)
            ? data.StateLabel
            : $"{data.StateLabel} {data.EventLabel}";
        lines.Add((stateLine, false));

        var weaponLabel = CsDemoPlayerAnimDebugResolver.FormatWeaponDisplayName(data.Weapon);
        if (!string.IsNullOrEmpty(weaponLabel))
        {
            lines.Add(($"wpn {weaponLabel}", false));
        }

        if (detailLevel >= 1 && !string.IsNullOrEmpty(data.ClipLabel))
        {
            lines.Add((data.ClipLabel, false));
        }

        if (detailLevel >= 1)
        {
            lines.Add((
                $"spd {data.Speed.ToString("0", CultureInfo.InvariantCulture)}  grd {(data.Grounded ? "Y" : "N")}  duck {data.DuckAmount.ToString("0.0", CultureInfo.InvariantCulture)}",
                false));
        }

        if (detailLevel >= 2)
        {
            lines.Add((
                $"aim {data.Pitch.ToString("0", CultureInfo.InvariantCulture)}/{data.Yaw.ToString("0", CultureInfo.InvariantCulture)}",
                false));
        }

        if (!string.IsNullOrEmpty(data.WarningLabel))
        {
            lines.Add((data.WarningLabel, true));
        }

        return lines;
    }

    private static float ResolvePanelWidth(PlayerAnimDebugData data, int detailLevel, IReadOnlyList<(string Text, bool IsWarning)> lines)
    {
        var baseWidth = detailLevel switch
        {
            0 => 156f,
            1 => 214f,
            _ => 252f,
        };

        var longestLine = lines.Max(static line => line.Text.Length);
        var contentWidth = 16f + (longestLine * (detailLevel == 0 ? 7.8f : detailLevel == 1 ? 8.6f : 9.4f));
        return Math.Max(baseWidth, contentWidth);
    }

    private static SKRect ClampToScreen(SKRect rect, int width, int height)
    {
        if (rect.Left < ScreenMargin)
        {
            rect.Offset(ScreenMargin - rect.Left, 0f);
        }

        if (rect.Right > width - ScreenMargin)
        {
            rect.Offset((width - ScreenMargin) - rect.Right, 0f);
        }

        if (rect.Top < ScreenMargin)
        {
            rect.Offset(0f, ScreenMargin - rect.Top);
        }

        if (rect.Bottom > height - ScreenMargin)
        {
            rect.Offset(0f, (height - ScreenMargin) - rect.Bottom);
        }

        return rect;
    }

    private static SKRect OffsetOverlapping(SKRect rect, IReadOnlyList<SKRect> occupiedRects)
    {
        const float step = 8f;
        const int maxAttempts = 8;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (!occupiedRects.Any(existing => existing.IntersectsWith(rect)))
            {
                return rect;
            }

            rect.Offset(0f, step);
        }

        return rect;
    }
}
