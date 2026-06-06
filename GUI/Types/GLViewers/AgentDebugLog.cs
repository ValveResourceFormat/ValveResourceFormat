using System.IO;
using System.Text.Json;

namespace GUI.Types.GLViewers;

static class AgentDebugLog
{
    private const string SessionId = "0ef808";
    private const string LogPath = @"c:\Users\ayden\Documents\Github Projects\cs2 viewer\ValveResourceFormat\debug-0ef808.log";

    public static void Write(string hypothesisId, string location, string message, object data)
    {
        try
        {
            var payload = new
            {
                sessionId = SessionId,
                runId = "pre-fix",
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            File.AppendAllText(LogPath, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Debug instrumentation must never affect demo playback.
        }
    }
}
